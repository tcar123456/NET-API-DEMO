namespace BookingApi.Domain;

// 報表查詢的結果列（唯讀），不是 EF Core 的 Entity
public record ProviderMonthlyRevenue(
    int ProviderId,
    string ProviderName,
    int BookingCount,
    int CancelledCount,
    decimal Revenue);
