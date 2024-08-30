﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Server.Documents.Handlers.Processors.Revisions;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Revisions
{
    internal class ShardedRevisionsHandlerProcessorForRevertRevisionsForDocument : AbstractRevisionsHandlerProcessorForRevertRevisionsForDocument<
        ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedRevisionsHandlerProcessorForRevertRevisionsForDocument([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override async Task RevertDocumentsAsync(Dictionary<string, string> idToChangeVector, OperationCancelToken token)
        {
            var shardsToDocs = new Dictionary<int, Dictionary<string, string>>();
            using (RequestHandler.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                foreach (var (id, cv) in idToChangeVector)
                {
                    var config = RequestHandler.DatabaseContext.DatabaseRecord.Sharding;
                    var shardNumber = ShardHelper.GetShardNumberFor(config, context, id);

                    if (shardsToDocs.ContainsKey(shardNumber) == false)
                        shardsToDocs[shardNumber] = new Dictionary<string, string>();
                    shardsToDocs[shardNumber].Add(id, cv);
                }
            }

            var op = new ShardedRevertRevisionsByIdOperation(shardsToDocs);
            await RequestHandler.ShardExecutor.ExecuteParallelForShardsAsync(shardsToDocs.Keys.ToArray(), op, token.Token);
        }
    }
}
