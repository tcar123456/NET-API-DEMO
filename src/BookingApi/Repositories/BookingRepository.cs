using BookingApi.Data;
using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly AppDbContext _db;

    public BookingRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Booking?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken)
    {
        return _db.Bookings
            .AsNoTracking()
            .Include(b => b.Provider)
            .Include(b => b.Service)
            .Include(b => b.Customer)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public Task<Booking?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken)
    {
        return _db.Bookings
            .Include(b => b.Provider)
            .Include(b => b.Service)
            .Include(b => b.Customer)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public Task<bool> HasActiveBookingAsync(int providerId, DateTime startTime, CancellationToken cancellationToken)
    {
        return _db.Bookings.AnyAsync(
            b => b.ProviderId == providerId
                && b.StartTime == startTime
                && b.Status == BookingStatus.Active,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DateTime>> GetActiveStartTimesAsync(
        int providerId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        return await _db.Bookings
            .Where(b => b.ProviderId == providerId
                && b.Status == BookingStatus.Active
                && b.StartTime >= from
                && b.StartTime < to)
            .Select(b => b.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Booking> Items, int TotalCount)> GetByCustomerAsync(
        int customerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _db.Bookings
            .AsNoTracking()
            .Where(b => b.CustomerId == customerId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Include(b => b.Provider)
            .Include(b => b.Service)
            .Include(b => b.Customer)
            // 加上 Id 作為第二排序鍵，確保同一時間的資料在分頁間順序固定，不會重複或遺漏
            .OrderByDescending(b => b.StartTime)
            .ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public void Add(Booking booking)
    {
        _db.Bookings.Add(booking);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
