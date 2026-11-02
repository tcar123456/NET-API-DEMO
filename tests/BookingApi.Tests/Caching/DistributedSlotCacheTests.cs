using BookingApi.Caching;
using BookingApi.Tests.TestDoubles;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookingApi.Tests.Caching;

public class DistributedSlotCacheTests
{
    private static readonly DateOnly Date = new(2026, 11, 20);

    private readonly FixedTimeProvider _time = FixedTimeProvider.AtTaiwanTime(new DateTime(2026, 11, 20, 9, 0, 0));

    [Fact]
    public async Task SetThenGet_ReturnsSameStartTimes()
    {
        var sut = CreateWithMemoryCache();
        var startTimes = new List<DateTime> { new(2026, 11, 20, 10, 0, 0), new(2026, 11, 20, 15, 0, 0) };

        await sut.SetBookedStartTimesAsync(1, Date, startTimes, CancellationToken.None);
        var cached = await sut.GetBookedStartTimesAsync(1, Date, CancellationToken.None);

        Assert.Equal(startTimes, cached);
    }

    [Fact]
    public async Task Get_DifferentProviderOrDate_ReturnsNull()
    {
        var sut = CreateWithMemoryCache();
        await sut.SetBookedStartTimesAsync(1, Date, new List<DateTime>(), CancellationToken.None);

        Assert.Null(await sut.GetBookedStartTimesAsync(2, Date, CancellationToken.None));
        Assert.Null(await sut.GetBookedStartTimesAsync(1, Date.AddDays(1), CancellationToken.None));
    }

    [Fact]
    public async Task Invalidate_RemovesEntry()
    {
        var sut = CreateWithMemoryCache();
        await sut.SetBookedStartTimesAsync(1, Date, new List<DateTime>(), CancellationToken.None);

        await sut.InvalidateAsync(1, Date, CancellationToken.None);

        Assert.Null(await sut.GetBookedStartTimesAsync(1, Date, CancellationToken.None));
    }

    [Fact]
    public async Task CacheUnavailable_GetReturnsNullAndWritesDoNotThrow()
    {
        var sut = new DistributedSlotCache(FailingCache(), _time, NullLogger<DistributedSlotCache>.Instance);

        Assert.Null(await sut.GetBookedStartTimesAsync(1, Date, CancellationToken.None));
        await sut.SetBookedStartTimesAsync(1, Date, new List<DateTime>(), CancellationToken.None);
        await sut.InvalidateAsync(1, Date, CancellationToken.None);
    }

    [Fact]
    public async Task AfterFailure_SkipsCacheFor30Seconds_ThenTriesAgain()
    {
        var failing = FailingCache();
        var sut = new DistributedSlotCache(failing, _time, NullLogger<DistributedSlotCache>.Instance);

        await sut.GetBookedStartTimesAsync(1, Date, CancellationToken.None);   // 失敗，開始斷路
        _time.Advance(TimeSpan.FromSeconds(29));
        await sut.GetBookedStartTimesAsync(1, Date, CancellationToken.None);   // 斷路中，不碰快取
        await sut.InvalidateAsync(1, Date, CancellationToken.None);            // 斷路中，不碰快取

        await failing.Received(1).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await failing.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromSeconds(2));
        await sut.GetBookedStartTimesAsync(1, Date, CancellationToken.None);   // 超過 30 秒，再試一次

        await failing.Received(2).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SlowCache_GivesUpAfterTimeoutAndReturnsNull()
    {
        // 模擬 Redis 連線卡住：操作一直不完成
        var hanging = Substitute.For<IDistributedCache>();
        hanging.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TaskCompletionSource<byte[]?>().Task);
        // 逾時要靠真的計時器，因此這裡使用系統時間
        var sut = new DistributedSlotCache(hanging, TimeProvider.System, NullLogger<DistributedSlotCache>.Instance);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await sut.GetBookedStartTimesAsync(1, Date, CancellationToken.None);

        Assert.Null(result);
        Assert.InRange(stopwatch.ElapsedMilliseconds, 400, 3000);
    }

    private DistributedSlotCache CreateWithMemoryCache()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new DistributedSlotCache(cache, _time, NullLogger<DistributedSlotCache>.Instance);
    }

    // 模擬 Redis 連不上：每個操作都拋出例外
    private static IDistributedCache FailingCache()
    {
        var failing = Substitute.For<IDistributedCache>();
        failing.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Redis is down"));
        failing.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Redis is down"));
        failing.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Redis is down"));
        return failing;
    }
}
