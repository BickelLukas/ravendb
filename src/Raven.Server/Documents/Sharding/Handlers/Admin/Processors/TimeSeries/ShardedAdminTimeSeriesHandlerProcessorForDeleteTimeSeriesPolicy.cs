﻿using JetBrains.Annotations;
using Raven.Server.Documents.Handlers.Admin.Processors.TimeSeries;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Sharding.Handlers.Admin.Processors.TimeSeries
{
    internal sealed class ShardedAdminTimeSeriesHandlerProcessorForDeleteTimeSeriesPolicy : AbstractAdminTimeSeriesHandlerProcessorForDeleteTimeSeriesPolicy<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedAdminTimeSeriesHandlerProcessorForDeleteTimeSeriesPolicy([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }
    }
}
