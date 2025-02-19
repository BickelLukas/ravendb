﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.ConnectionStrings;
using Raven.Client.Documents.Operations.ETL.Queue;
using Raven.Client.Documents.Operations.QueueSink;
using Raven.Client.Util;
using Raven.Server.Documents.QueueSink;
using Raven.Server.NotificationCenter;
using Raven.Server.NotificationCenter.Notifications;
using Raven.Server.NotificationCenter.Notifications.Details;
using Sparrow.Json;
using Xunit;
using Xunit.Abstractions;
using QueueSinkConfiguration = Raven.Client.Documents.Operations.QueueSink.QueueSinkConfiguration;

namespace SlowTests.Server.Documents.QueueSink
{
    [Trait("Category", "QueueSink")]
    public abstract class QueueSinkTestBase : RavenTestBase
    {
        protected QueueSinkTestBase(ITestOutputHelper output) : base(output)
        {
            QueueSuffix = Guid.NewGuid().ToString("N");
        }

        protected string QueueSuffix { get; }

        protected string UsersQueueName => $"users{QueueSuffix}";

        protected List<string> DefaultQueues => new() { UsersQueueName };

        protected AddQueueSinkOperationResult AddQueueSink<T>(DocumentStore src, QueueSinkConfiguration configuration, T connectionString) where T : ConnectionString
        {
            var putResult = src.Maintenance.Send(new PutConnectionStringOperation<T>(connectionString));
            Assert.NotNull(putResult.RaftCommandIndex);

            var addResult = src.Maintenance.Send(new AddQueueSinkOperation<T>(configuration));
            return addResult;
        }

        private async Task<string[]> GetQueueSinkErrorNotifications(DocumentStore src)
        {
            var databaseInstanceFor = await Databases.GetDocumentDatabaseInstanceFor(src);
            using (databaseInstanceFor.NotificationCenter.GetStored(out IEnumerable<NotificationTableValue> storedNotifications, postponed: false))
            {
                var notifications = storedNotifications
                    .Select(n => n.Json)
                    .Where(n => n.TryGet("AlertType", out string type) && type.StartsWith("QueueSink_"))
                    .Where(n => n.TryGet("Details", out BlittableJsonReaderObject _))
                    .Select(n =>
                    {
                        n.TryGet("Details", out BlittableJsonReaderObject details);
                        return details.ToString();
                    }).ToArray();
                return notifications;
            }
        }
        
        public async Task<QueueSinkErrorInfo> TryErrorFromAlertAsync(string databaseName, QueueSinkConfiguration config)
        {
            var database = await GetDatabase(databaseName);

            string tag = config.BrokerType == QueueBrokerType.Kafka ? QueueSinkProcess.KafkaTag : QueueSinkProcess.RabbitMqTag;

            var errorAlert = database.NotificationCenter.QueueSinkNotifications.GetAlert<QueueSinkErrorsDetails>(tag, $"{config.Name}/{config.Scripts.First().Name}", AlertType.QueueSink_Error);
            var consumeErrorAlert = database.NotificationCenter.QueueSinkNotifications.GetAlert<QueueSinkErrorsDetails>(tag, $"{config.Name}/{config.Scripts.First().Name}", AlertType.QueueSink_ConsumeError);
            var scriptErrorAlert = database.NotificationCenter.QueueSinkNotifications.GetAlert<QueueSinkErrorsDetails>(tag, $"{config.Name}/{config.Scripts.First().Name}", AlertType.QueueSink_ScriptError);

            if (errorAlert.Errors.Count != 0)
            {
                return errorAlert.Errors.First();
            }
            if (consumeErrorAlert.Errors.Count != 0)
            {
                return consumeErrorAlert.Errors.First();
            }
            if (scriptErrorAlert.Errors.Count != 0)
            {
                return scriptErrorAlert.Errors.First();
            }

            return null;
        }
        
        protected ManualResetEventSlim WaitForQueueSinkBatch(DocumentStore store,
            Func<string, QueueSinkProcessStatistics, bool> predicate)
        {
            var database = AsyncHelpers.RunSync(() => GetDatabase(store.Database));

            var mre = new ManualResetEventSlim();

            database.QueueSinkLoader.BatchCompleted += x =>
            {
                if (predicate($"{x.ConfigurationName}/{x.ScriptName}", x.Statistics))
                    mre.Set();
            };

            return mre;
        }

        protected void AssertQueueSinkDone(ManualResetEventSlim etlDone, TimeSpan timeout, string databaseName, QueueSinkConfiguration config)
        {
            if (etlDone.Wait(timeout) == false)
            {
                var error = AsyncHelpers.RunSync(() => TryErrorFromAlertAsync(databaseName, config));

                Assert.Fail($"Queue Sink wasn't done. Error: {error?.Error}");
            }
        }

        protected class User
        {
            public string Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }

            public string FullName { get; set; }
        }
    }
}
