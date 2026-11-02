namespace BookingApi.Services;

public static class TimeProviderExtensions
{
    // 台灣沒有日光節約時間，固定 UTC+8，不需要查時區資料庫
    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);

    public static DateTime GetTaiwanNow(this TimeProvider timeProvider)
    {
        return timeProvider.GetUtcNow().ToOffset(TaiwanOffset).DateTime;
    }
}
