using BookingApi.Dtos;
using BookingApi.Exceptions;
using BookingApi.Repositories;

namespace BookingApi.Services;

public class ReportService : IReportService
{
    private readonly IReportRepository _reportRepository;

    public ReportService(IReportRepository reportRepository)
    {
        _reportRepository = reportRepository;
    }

    public async Task<MonthlyRevenueReportResponse> GetMonthlyRevenueAsync(
        int year, int month, CancellationToken cancellationToken)
    {
        if (year < 2000 || year > 2100)
        {
            throw new BusinessRuleException("Year must be between 2000 and 2100.");
        }

        if (month < 1 || month > 12)
        {
            throw new BusinessRuleException("Month must be between 1 and 12.");
        }

        // 半開區間 [當月 1 日, 下月 1 日)：不需要處理每月天數與 23:59:59.9999999 的邊界
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1);

        var rows = await _reportRepository.GetProviderRevenueAsync(from, to, cancellationToken);

        var providers = rows
            .Select(r => new ProviderRevenueResponse(r.ProviderId, r.ProviderName, r.BookingCount, r.CancelledCount, r.Revenue))
            .ToList();

        return new MonthlyRevenueReportResponse(
            year,
            month,
            providers.Sum(p => p.BookingCount),
            providers.Sum(p => p.Revenue),
            providers);
    }
}
