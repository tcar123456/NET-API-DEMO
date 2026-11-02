using BookingApi.Dtos;

namespace BookingApi.Services;

public interface IReportService
{
    Task<MonthlyRevenueReportResponse> GetMonthlyRevenueAsync(int year, int month, CancellationToken cancellationToken);
}
