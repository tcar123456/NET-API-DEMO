using BookingApi.Domain;
using BookingApi.Exceptions;
using BookingApi.Repositories;
using BookingApi.Services;
using NSubstitute;

namespace BookingApi.Tests.Services;

public class ReportServiceTests
{
    private readonly IReportRepository _reportRepository = Substitute.For<IReportRepository>();
    private readonly ReportService _sut;

    public ReportServiceTests()
    {
        _sut = new ReportService(_reportRepository);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(1999, 11)]
    [InlineData(2101, 11)]
    public async Task GetMonthlyRevenueAsync_InvalidYearOrMonth_ThrowsBusinessRuleException(int year, int month)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.GetMonthlyRevenueAsync(year, month, CancellationToken.None));

        await _reportRepository.DidNotReceive().GetProviderRevenueAsync(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2026, 11, "2026-11-01", "2026-12-01")]
    [InlineData(2026, 12, "2026-12-01", "2027-01-01")]   // 跨年
    [InlineData(2028, 2, "2028-02-01", "2028-03-01")]    // 閏年二月
    public async Task GetMonthlyRevenueAsync_QueriesHalfOpenMonthRange(int year, int month, string expectedFrom, string expectedTo)
    {
        _reportRepository
            .GetProviderRevenueAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProviderMonthlyRevenue>());

        await _sut.GetMonthlyRevenueAsync(year, month, CancellationToken.None);

        await _reportRepository.Received(1).GetProviderRevenueAsync(
            DateTime.Parse(expectedFrom), DateTime.Parse(expectedTo), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMonthlyRevenueAsync_SumsTotalsAndKeepsProviderOrder()
    {
        _reportRepository
            .GetProviderRevenueAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProviderMonthlyRevenue>
            {
                new(2, "B", BookingCount: 3, CancelledCount: 1, Revenue: 3600m),
                new(1, "A", BookingCount: 2, CancelledCount: 0, Revenue: 2100m),
                new(3, "C", BookingCount: 0, CancelledCount: 2, Revenue: 0m),
            });

        var report = await _sut.GetMonthlyRevenueAsync(2026, 11, CancellationToken.None);

        Assert.Equal(2026, report.Year);
        Assert.Equal(11, report.Month);
        Assert.Equal(5, report.TotalBookings);
        Assert.Equal(5700m, report.TotalRevenue);
        Assert.Equal(new[] { 2, 1, 3 }, report.Providers.Select(p => p.ProviderId));
        Assert.Equal(2, report.Providers[2].CancelledCount);
    }
}
