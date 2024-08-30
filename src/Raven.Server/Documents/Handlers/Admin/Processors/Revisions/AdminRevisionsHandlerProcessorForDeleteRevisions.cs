﻿using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Operations.Revisions;
using Raven.Client.Extensions;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using static Raven.Server.Documents.Revisions.RevisionsStorage;

namespace Raven.Server.Documents.Handlers.Admin.Processors.Revisions
{
    internal sealed class AdminRevisionsHandlerProcessorForDeleteRevisions : AbstractAdminRevisionsHandlerProcessorForDeleteRevisions<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public AdminRevisionsHandlerProcessorForDeleteRevisions([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override Task<long> DeleteRevisionsAsync(DeleteRevisionsOperation.Parameters request, OperationCancelToken token)
        {
            if (request.RevisionsChangeVectors.IsNullOrEmpty() == false)
                return DeleteRevisionsByChangeVectorAsync(request.DocumentIds.Single(), request.RevisionsChangeVectors, request.RemoveForceCreatedRevisions);

            return DeleteRevisionsByDocumentIdAsync(request.DocumentIds, request.From, request.To, request.RemoveForceCreatedRevisions, token);
        }

        private async Task<long> DeleteRevisionsByChangeVectorAsync(string id, List<string> cvs, bool includeForceCreated)
        {
            var cmd = new DeleteRevisionsByChangeVectorMergedCommand(id, cvs, includeForceCreated);
            await RequestHandler.Database.TxMerger.Enqueue(cmd);
            return cmd.Result.HasValue ? cmd.Result.Value : 0;
        }

        private async Task<long> DeleteRevisionsByDocumentIdAsync(List<string> ids, DateTime? from, DateTime? to, bool includeForceCreated, OperationCancelToken token)
        {
            var deleted = 0L;
            var moreWork = false;

            do
            {
                token.ThrowIfCancellationRequested();

                var cmd = new DeleteRevisionsByDocumentIdMergedCommand(ids, from, to, includeForceCreated);
                await RequestHandler.Database.TxMerger.Enqueue(cmd);

                if (cmd.Result.HasValue)
                {
                    deleted += cmd.Result.Value.Deleted;
                    moreWork = cmd.Result.Value.MoreWork;
                }
            } while(moreWork);

            return deleted;
        }

    }
}
