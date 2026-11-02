namespace BookingApi.Caching;

// 快取「某師傅某天已被預約的開始時間」，用來加速可預約時段查詢
public interface ISlotCache
{
    // 沒有快取（或快取無法使用）時回傳 null，呼叫端改查資料庫
    Task<IReadOnlyList<DateTime>?> GetBookedStartTimesAsync(int providerId, DateOnly date, CancellationToken cancellationToken);

    Task SetBookedStartTimesAsync(
        int providerId, DateOnly date, IReadOnlyList<DateTime> startTimes, CancellationToken cancellationToken);

    Task InvalidateAsync(int providerId, DateOnly date, CancellationToken cancellationToken);
}
