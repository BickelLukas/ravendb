﻿using System;
using System.Collections.Generic;
using Raven.Server.Documents.TransactionMerger.Commands;
using Raven.Server.ServerWide.Context;
using Voron.Data.Tables;
using Voron;

namespace Raven.Server.Documents.Revisions;
public partial class RevisionsStorage
{
    internal sealed class DeleteRevisionsByChangeVectorMergedCommand : MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>
    {
        private readonly List<string> _cvs;

        public long? Result { get; private set; } // number of deleted revisions

        public DeleteRevisionsByChangeVectorMergedCommand(List<string> cvs)
        {
            _cvs = cvs;
        }

        protected override long ExecuteCmd(DocumentsOperationContext context)
        {
            Result = DeleteRevisions(context);
            return 1;
        }

        private long DeleteRevisions(DocumentsOperationContext context)
        {
            var revisionsStorage = context.DocumentDatabase.DocumentsStorage.RevisionsStorage;

            var deleted = 0L;

            var lastModifiedTicks = context.DocumentDatabase.Time.GetUtcNow().Ticks;

            var table = new Table(revisionsStorage.RevisionsSchema, context.Transaction.InnerTransaction);

            var writeTables = new Dictionary<string, Table>();

            foreach (var cv in _cvs)
            {
                if (string.IsNullOrEmpty(cv))
                    throw new ArgumentException("Change Vector is null or empty");

                Document revision;
                using (Slice.From(context.Allocator, cv, out var cvSlice))
                {
                    if (table.ReadByKey(cvSlice, out TableValueReader tvr) == false)
                    {
                        continue;
                    }

                    revision = TableValueToRevision(context, ref tvr, DocumentFields.ChangeVector | DocumentFields.LowerId);
                }

                using (DocumentIdWorker.GetSliceFromId(context, revision.LowerId, out var lowerId))
                using (revisionsStorage.GetKeyPrefix(context, lowerId, out var lowerIdPrefix))
                {
                    var collectionName = revisionsStorage.GetCollectionFor(context, lowerIdPrefix);
                    if (collectionName == null)
                    {
                        if (revisionsStorage._logger.IsInfoEnabled)
                            revisionsStorage._logger.Info($"Tried to delete revision {revision.ChangeVector} ({revision.LowerId}) but no collection found.");
                        continue;
                    }

                    if (context.DocumentDatabase.DocumentsStorage.RevisionsStorage.IsAllowedToDeleteRevisionsManually(collectionName.Name, revision.Flags) == false)
                        throw new InvalidOperationException(
                            $"You are trying to delete revisions of '{revision.LowerId}' but it isn't allowed by its revisions configuration.");

                    var collectionTable = revisionsStorage.EnsureRevisionTableCreated(context.Transaction.InnerTransaction, collectionName);

                    revisionsStorage.DeleteRevisionFromTable(context, collectionTable, writeTables, revision, collectionName, context.GetChangeVector(cv), lastModifiedTicks, revision.Flags);
                    RevisionsStorage.IncrementCountOfRevisions(context, lowerIdPrefix, -1);
                    deleted++;
                }
            }

            return deleted;
        }

        public override IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, MergedTransactionCommand<DocumentsOperationContext, DocumentsTransaction>> ToDto(DocumentsOperationContext context)
        {
            return new DeleteRevisionsByChangeVectorMergedCommandDto(_cvs);
        }

        private sealed class DeleteRevisionsByChangeVectorMergedCommandDto : IReplayableCommandDto<DocumentsOperationContext, DocumentsTransaction, DeleteRevisionsByChangeVectorMergedCommand>
        {
            private readonly List<string> _cvs;

            public DeleteRevisionsByChangeVectorMergedCommandDto(List<string> cvs)
            {
                _cvs = cvs;
            }

            public DeleteRevisionsByChangeVectorMergedCommand ToCommand(DocumentsOperationContext context, DocumentDatabase database)
            {
                return new DeleteRevisionsByChangeVectorMergedCommand(_cvs);
            }
        }
    }

}
