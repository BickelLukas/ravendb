using System;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;

namespace Raven.Server.Utils.Imports.Memory;

public sealed class MemoryCacheOptions : IOptions<MemoryCacheOptions>
{
    private long? _sizeLimit;
    private double _compactionPercentage = 0.05;

    public ISystemClock Clock { get; set; }

    /// <summary>
    /// Gets or sets the minimum length of time between successive scans for expired items.
    /// </summary>
    public TimeSpan ExpirationScanFrequency { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum size of the cache.
    /// </summary>
    public long? SizeLimit
    {
        get => _sizeLimit;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(value)} must be non-negative.");
            }

            _sizeLimit = value;
        }
    }

    /// <summary>
    /// Gets or sets the amount to compact the cache by when the maximum size is exceeded.
    /// </summary>
    public double CompactionPercentage
    {
        get => _compactionPercentage;
        set
        {
            if (value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(value)} must be between 0 and 1 inclusive.");
            }

            _compactionPercentage = value;
        }
    }

    /// <summary>
    /// Gets or sets whether to track linked entries. Disabled by default.
    /// </summary>
    /// <remarks>Prior to .NET 7 this feature was always enabled.</remarks>
    public bool TrackLinkedCacheEntries { get; set; }

    MemoryCacheOptions IOptions<MemoryCacheOptions>.Value
    {
        get { return this; }
    }
}
