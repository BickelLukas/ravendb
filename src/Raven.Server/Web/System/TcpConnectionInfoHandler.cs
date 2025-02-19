﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Raven.Client.Exceptions.Cluster;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide;
using Raven.Server.Documents;
using Raven.Server.Extensions;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Web.System
{
    public sealed class TcpConnectionInfoHandler : ServerRequestHandler
    {
        [RavenAction("/info/tcp", "GET", AuthorizationStatus.ValidUser, EndpointType.Read)]
        public async Task Get()
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var output = Server.ServerStore.GetTcpInfoAndCertificates(HttpContext.Request.GetClientRequestedNodeUrl());
                context.Write(writer, output);
            }
        }

        [RavenAction("/info/remote-task/topology", "GET", AuthorizationStatus.RestrictedAccess)]
        public async Task GetRemoteTaskTopology()
        {
            var database = GetStringQueryString("database");
            var databaseGroupId = GetStringQueryString("groupId");
            var remoteTask = GetStringQueryString("remote-task");

            var authResult = await AuthenticateAsync(HttpContext, ServerStore, database, remoteTask).ConfigureAwait(false);
            if (authResult == false)
                return;

            List<string> nodes;
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                var pullReplication = ServerStore.Cluster.ReadPullReplicationDefinition(database, remoteTask, context);
                if (pullReplication.Disabled)
                    throw new InvalidOperationException($"The pull replication '{remoteTask}' is disabled.");

                var topology = ServerStore.Cluster.ReadDatabaseTopology(context, database);
                nodes = GetResponsibleNodes(topology, databaseGroupId, pullReplication.MentorNode, pullReplication.PinToMentorNode);
            }

            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var output = new DynamicJsonArray();
                var clusterTopology = ServerStore.GetClusterTopology();
                foreach (var node in nodes)
                {
                    output.Add(clusterTopology.GetUrlFromTag(node));
                }
                context.Write(writer, new DynamicJsonValue
                {
                    ["Results"] = output
                });
            }
        }

        [RavenAction("/info/remote-task/tcp", "GET", AuthorizationStatus.RestrictedAccess)]
        public async Task GetRemoteTaskTcp()
        {
            var remoteTask = GetStringQueryString("remote-task");
            var database = GetStringQueryString("database");
            var verifyDatabase = GetBoolValueQueryString("verify-database", false);

            if (ServerStore.IsPassive())
            {
                throw new NodeIsPassiveException($"Can't fetch Tcp info from a passive node in url {this.HttpContext.Request.GetFullUrl()}");
            }

            if (verifyDatabase.HasValue && verifyDatabase.Value)
            {
                var result = ServerStore.DatabasesLandlord.TryGetOrCreateDatabase(database);
                switch (result.DatabaseStatus)
                {
                    case DatabasesLandlord.DatabaseSearchResult.Status.Database:
                    case DatabasesLandlord.DatabaseSearchResult.Status.Sharded:
                        break;
                    case DatabasesLandlord.DatabaseSearchResult.Status.Missing:
                        DatabaseDoesNotExistException.Throw(database);
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected " + nameof(DatabasesLandlord.DatabaseSearchResult));
                }
            }

            var authResult = await AuthenticateAsync(HttpContext, ServerStore, database, remoteTask).ConfigureAwait(false);
            if (authResult == false)
                return;

            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var output = Server.ServerStore.GetTcpInfoAndCertificates(HttpContext.Request.GetClientRequestedNodeUrl(), forExternalUse: true);
                context.Write(writer, output);
            }
        }

        public static async ValueTask<bool> AuthenticateAsync(HttpContext httpContext, ServerStore serverStore, string database, string remoteTask)
        {
            var feature = httpContext.Features.Get<IHttpAuthenticationFeature>() as RavenServer.AuthenticateConnection;

            if (feature == null) // we are not using HTTPS
                return true;

            switch (feature.Status)
            {
                case RavenServer.AuthenticationStatus.Operator:
                case RavenServer.AuthenticationStatus.ClusterAdmin:
                    // we can trust this certificate
                    return true;

                case RavenServer.AuthenticationStatus.Allowed:
                    // check that the certificate is allowed for this database.
                    if (feature.CanAccess(database, requireAdmin: false, requireWrite: false))
                        return true;

                    await RequestRouter.UnlikelyFailAuthorizationAsync(httpContext, database, feature, AuthorizationStatus.RestrictedAccess)
                                       .ConfigureAwait(false);
                    return false;

                case RavenServer.AuthenticationStatus.UnfamiliarIssuer:
                    await RequestRouter.UnlikelyFailAuthorizationAsync(httpContext, database, feature, AuthorizationStatus.RestrictedAccess)
                                       .ConfigureAwait(false);
                    return false;

                case RavenServer.AuthenticationStatus.UnfamiliarCertificate:
                    using (serverStore.Engine.ContextPool.AllocateOperationContext(out ClusterOperationContext context))
                    using (context.OpenReadTransaction())
                    {
                        if (serverStore.Cluster.TryReadPullReplicationDefinition(database, remoteTask, context, out var pullReplication))
                        {
                            var cert = httpContext.Connection.ClientCertificate;

                            if (serverStore.Cluster.IsReplicationCertificate(context, database, remoteTask, cert, out _))
                                return true;

                            if (serverStore.Cluster.IsReplicationCertificateByPublicKeyPinningHash(context, database, remoteTask, cert, serverStore.Configuration.Security, out _))
                                return true;
                        }

                        await RequestRouter.UnlikelyFailAuthorizationAsync(httpContext, database, feature, AuthorizationStatus.RestrictedAccess)
                                           .ConfigureAwait(false);
                        return false;
                    }

                default:
                    throw new ArgumentException($"This is a bug, we should deal with '{feature?.Status}' authentication status at RequestRoute.TryAuthorize function.");
            }
        }

        private List<string> GetResponsibleNodes(DatabaseTopology topology, string databaseGroupId, string mentorNode, bool pinToMentorNode)
        {
            // we distribute connections to have load balancing when many sinks are connected.
            // this is the hub cluster, so we make the decision which node will do the pull replication only once and only here,
            // for that we create a dummy IDatabaseTask.
            var mentorNodeTask = new PullNodeTask
            {
                Mentor = mentorNode,
                PinToMentorNode = pinToMentorNode,
                DatabaseGroupId = databaseGroupId
            };

            if (pinToMentorNode)
            {
                if (topology.AllNodes.Contains(mentorNode))
                    return new List<string> { mentorNode };
            }

            var list = new List<string>();
            while (topology.Members.Count > 0)
            {
                var next = topology.WhoseTaskIsIt(ServerStore.CurrentRachisState, mentorNodeTask, null);
                list.Add(next);
                topology.Members.Remove(next);
            }
            return list;
        }

        private sealed class PullNodeTask : IDatabaseTask
        {
            public string Mentor;
            public string DatabaseGroupId;
            public bool PinToMentorNode;

            public ulong GetTaskKey()
            {
                return Hashing.Mix(Hashing.XXHash64.Calculate(DatabaseGroupId, Encodings.Utf8));
            }

            public string GetMentorNode()
            {
                return Mentor;
            }

            public string GetDefaultTaskName()
            {
                throw new NotImplementedException();
            }

            public string GetTaskName()
            {
                throw new NotImplementedException();
            }

            public bool IsResourceIntensive()
            {
                return false;
            }

            public bool IsPinnedToMentorNode()
            {
                return PinToMentorNode;
            }
        }
    }
}
