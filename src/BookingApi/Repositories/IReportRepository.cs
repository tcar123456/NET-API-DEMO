using BookingApi.Domain;

namespace BookingApi.Repositories;

public interface IReportRepository
{
    // 統計 [from, to) 期間內每位師傅的預約數與營收
    Task<IReadOnlyList<ProviderMonthlyRevenue>> GetProviderRevenueAsync(
        DateTime from, DateTime to, CancellationToken cancellationToken);
}
