namespace BookingApi.Tests.TestDoubles;

// 讓「現在時間」固定，測試結果不會因為執行的日期或時間而改變
public class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    private FixedTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public static FixedTimeProvider AtTaiwanTime(DateTime taiwanTime)
    {
        return new FixedTimeProvider(new DateTimeOffset(taiwanTime, TimeSpan.FromHours(8)));
    }

    public override DateTimeOffset GetUtcNow() => _utcNow.ToUniversalTime();
}
