using System;
using Raven.Client.Documents.Session;

namespace Raven.Client.Documents.Queries.Suggestions
{
    /// <inheritdoc cref="SuggestionDocumentQuery{T}"/>
    internal sealed class AsyncSuggestionDocumentQuery<T> : SuggestionQueryBase, IAsyncSuggestionDocumentQuery<T>
    {
        private readonly AsyncDocumentQuery<T> _source;

        /// <inheritdoc cref="SuggestionDocumentQuery{T}"/>
        public AsyncSuggestionDocumentQuery(AsyncDocumentQuery<T> source)
            : base((InMemoryDocumentSessionOperations)source.AsyncSession)
        {
            _source = source;
        }

        protected override IndexQuery GetIndexQuery(bool isAsync, bool updateAfterQueryExecuted = true)
        {
            return _source.GetIndexQuery();
        }

        protected override void InvokeAfterQueryExecuted(QueryResult result)
        {
            _source.InvokeAfterQueryExecuted(result);
        }
        
        /// <inheritdoc cref="SuggestionDocumentQuery{T}.AndSuggestUsing(Raven.Client.Documents.Queries.Suggestions.SuggestionBase)"/>
        public IAsyncSuggestionDocumentQuery<T> AndSuggestUsing(SuggestionBase suggestion)
        {
            _source.SuggestUsing(suggestion);
            return this;
        }

        /// <inheritdoc cref="ISuggestionQuery{T}.AndSuggestUsing(Action{ISuggestionBuilder{T}})"/>
        public IAsyncSuggestionDocumentQuery<T> AndSuggestUsing(Action<ISuggestionBuilder<T>> builder)
        {
            var f = new SuggestionBuilder<T>(_source.Conventions);
            builder.Invoke(f);

            _source.SuggestUsing(f.Suggestion);
            return this;
        }
    }
}
