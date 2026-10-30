using Microsoft.Extensions.Caching.Memory;

namespace DisputeDesk.Api.Linking.RateLimiting;

/// <summary>
/// Which IP and login partitions were rejected in the last window, so only the first rejection of each is audited (FR-10).
/// One entry per limiter and partition key, expiring one window after that partition's first rejection, in a cache of
/// its own with a <c>SizeLimit</c>, so rotating IPv6 prefixes cannot grow it without bound (Microsoft Learn, "Cache
/// in-memory in ASP.NET Core": use SetSize, Size and SizeLimit). A full cache stores no new entry and compacts itself to
/// 95% of the limit in the background, so a partition that found it full is audited again on its next rejection, and
/// then stored once there is room.
/// </summary>
public sealed class FirstRejections : IDisposable
{
    public const int MaximumEntries = 10_000;

    private readonly MemoryCache _rejected = new(new MemoryCacheOptions { SizeLimit = MaximumEntries });

    /// <summary>
    /// True when this rejection is the first in the window for its IP partition or its login partition, and records
    /// whichever was absent. Two parallel first rejections can both get true; a rare double audit is accepted.
    /// </summary>
    public bool Record(string ipPartition, string loginPartition, TimeSpan window)
    {
        bool ipIsNew = Add("ip:" + ipPartition, window);
        bool loginIsNew = Add("login:" + loginPartition, window);
        return ipIsNew || loginIsNew;
    }

    public void Dispose() => _rejected.Dispose();

    private bool Add(string key, TimeSpan window)
    {
        if (_rejected.TryGetValue(key, out _))
        {
            return false;
        }

        _rejected.Set(key, true, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = window });
        return true;
    }
}
