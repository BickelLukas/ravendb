﻿using System;
using System.Threading;
using Raven.Server.Documents.Indexes.Persistence.Lucene;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Indexes.Workers
{
    public interface IIndexingWork : IDisposable
    {
        bool Execute(DocumentsOperationContext databaseContext, TransactionOperationContext indexContext,
                     Lazy<IndexWriteOperation> writeOperation, IndexingBatchStats stats, CancellationToken token);
    }
}