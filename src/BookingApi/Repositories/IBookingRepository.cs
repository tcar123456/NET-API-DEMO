using BookingApi.Domain;

namespace BookingApi.Repositories;

public interface IBookingRepository
{
    // 唯讀查詢，含師傅、服務、客戶資料
    Task<Booking?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken);

    // 會被 EF Core 追蹤，修改後呼叫 SaveChangesAsync 寫回資料庫
    Task<Booking?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken);

    Task<bool> HasActiveBookingAsync(int providerId, DateTime startTime, CancellationToken cancellationToken);

    Task<IReadOnlyList<DateTime>> GetActiveStartTimesAsync(
        int providerId, DateTime from, DateTime to, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Booking> Items, int TotalCount)> GetByCustomerAsync(
        int customerId, int page, int pageSize, CancellationToken cancellationToken);

    void Add(Booking booking);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
