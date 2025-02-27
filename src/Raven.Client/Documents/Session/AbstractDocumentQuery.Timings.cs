using Raven.Client.Documents.Queries.Timings;

namespace Raven.Client.Documents.Session
{
    public abstract partial class AbstractDocumentQuery<T, TSelf>
    {
        protected QueryTimings QueryTimings = new QueryTimings();

        /// <inheritdoc cref="IDocumentQueryCustomization.Timings" />
        public void IncludeTimings(out QueryTimings timings)
        {
            QueryTimings.ShouldBeIncluded = true;
            timings = QueryTimings;
        }
    }
}
