using System.Diagnostics.Metrics;
using Lextm.SharpSnmpLib;
using Raven.Server.Documents;
using Raven.Server.Monitoring.OpenTelemetry;
using Raven.Server.ServerWide;

namespace Raven.Server.Monitoring.Snmp.Objects.Database
{
    public sealed class TotalDatabaseMapIndexIndexedPerSecond : DatabaseBase<Gauge32>, IMetricInstrument<int>
    {
        public TotalDatabaseMapIndexIndexedPerSecond(ServerStore serverStore)
            : base(serverStore, SnmpOids.Databases.General.TotalMapIndexIndexesPerSecond)
        {
        }

        private int Value
        {
            get
            {
                var count = 0;
                foreach (var database in GetLoadedDatabases())
                    count += GetCountSafely(database, GetCount);
                return count;
            }
        }

        protected override Gauge32 GetData()
        {
            return new Gauge32(Value);
        }

        private static int GetCount(DocumentDatabase database)
        {
            return (int)database.Metrics.MapIndexes.IndexedPerSec.OneMinuteRate;
        }

        public int GetCurrentMeasurement() => Value;
    }
}
