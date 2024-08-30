﻿using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Lextm.SharpSnmpLib;
using Raven.Server.Monitoring.OpenTelemetry;
using Sparrow;
using Sparrow.LowMemory;

namespace Raven.Server.Monitoring.Snmp.Objects.Server
{
    public sealed class ServerManagedMemory() : ScalarObjectBase<Gauge32>(SnmpOids.Server.ManagedMemory), IMetricInstrument<long>
    {
        private long Value
        {
            get
            {
                var managedMemoryInBytes = AbstractLowMemoryMonitor.GetManagedMemoryInBytes();
                return new Size(managedMemoryInBytes, SizeUnit.Bytes).GetValue(SizeUnit.Megabytes);
            }
        }
        
        protected override Gauge32 GetData()
        {
            return new Gauge32(Value);
        }

        public long GetCurrentMeasurement() => Value;
    }
}
