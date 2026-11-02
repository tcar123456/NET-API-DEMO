namespace BookingApi.Dtos;

public record MonthlyRevenueReportResponse(
    int Year,
    int Month,
    int TotalBookings,
    decimal TotalRevenue,
    IReadOnlyList<ProviderRevenueResponse> Providers);

public record ProviderRevenueResponse(
    int ProviderId,
    string ProviderName,
    int BookingCount,
    int CancelledCount,
    decimal Revenue);
