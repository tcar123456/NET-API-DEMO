using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace BookingApi.Caching;

// 以 IDistributedCache 實作：正式環境是 Redis，沒有設定 Redis 時是記憶體快取
public class DistributedSlotCache : ISlotCache
{
    // 保底的存活時間：即使漏掉一次失效（例如讀寫交錯），最多 5 分鐘後也會重新從資料庫讀取
    private static readonly DistributedCacheEntryOptions EntryOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    // 簡易斷路器：快取操作失敗後，這段時間內直接略過快取，
    // 否則 Redis 停機時每個請求都要等連線逾時（實測未加斷路器時每次查詢約 11 秒）
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    // 每次快取操作最多等這麼久。本機 Redis 正常時約 1 毫秒；
    // Redis 停機時連線逾時不一定受 Redis 用戶端設定控制（實測第一次仍要 7 秒），因此在這裡再設上限
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IDistributedCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DistributedSlotCache> _logger;

    // 斷路到期時間（UTC ticks）。這個類別是 Singleton，多個請求同時讀寫，因此用 Interlocked
    private long _skipUntilUtcTicks;

    public DistributedSlotCache(IDistributedCache cache, TimeProvider timeProvider, ILogger<DistributedSlotCache> logger)
    {
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // 快取只是加速：無法使用時回傳 null，讓呼叫端改查資料庫，而不是讓 API 失敗
    public async Task<IReadOnlyList<DateTime>?> GetBookedStartTimesAsync(
        int providerId, DateOnly date, CancellationToken cancellationToken)
    {
        if (IsBroken())
        {
            return null;
        }

        try
        {
            var bytes = await _cache.GetAsync(Key(providerId, date), cancellationToken)
                .WaitAsync(OperationTimeout, _timeProvider, cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<List<DateTime>>(bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Break(exception, "read", providerId, date);
            return null;
        }
    }

    public async Task SetBookedStartTimesAsync(
        int providerId, DateOnly date, IReadOnlyList<DateTime> startTimes, CancellationToken cancellationToken)
    {
        if (IsBroken())
        {
            return;
        }

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(startTimes);
            await _cache.SetAsync(Key(providerId, date), bytes, EntryOptions, cancellationToken)
                .WaitAsync(OperationTimeout, _timeProvider, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Break(exception, "write", providerId, date);
        }
    }

    public async Task InvalidateAsync(int providerId, DateOnly date, CancellationToken cancellationToken)
    {
        if (IsBroken())
        {
            return;
        }

        try
        {
            await _cache.RemoveAsync(Key(providerId, date), cancellationToken)
                .WaitAsync(OperationTimeout, _timeProvider, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Break(exception, "invalidate", providerId, date);
        }
    }

    public static string Key(int providerId, DateOnly date) => $"slots:{providerId}:{date:yyyy-MM-dd}";

    private bool IsBroken()
    {
        return _timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref _skipUntilUtcTicks);
    }

    private void Break(Exception exception, string operation, int providerId, DateOnly date)
    {
        Interlocked.Exchange(ref _skipUntilUtcTicks, (_timeProvider.GetUtcNow() + BreakDuration).UtcTicks);
        _logger.LogWarning(exception,
            "Slot cache {Operation} failed for provider {ProviderId} on {Date}; skipping cache for {Seconds} seconds",
            operation, providerId, date, BreakDuration.TotalSeconds);
    }
}
