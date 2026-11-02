using BookingApi.Data;
using BookingApi.Domain;
using BookingApi.Exceptions;
using Microsoft.Data.SqlClient;
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

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // UPDATE 的 WHERE 條件帶有讀取時的 RowVersion，影響 0 筆代表讀取後已被其他請求修改
            throw new ConflictException(
                "The booking was modified by another request. Please reload and try again.", exception);
        }
        catch (DbUpdateException exception) when (IsUniqueIndexViolation(exception))
        {
            // 並發時多個請求都可能通過 Service 的「先查再寫」檢查，最終由唯一索引保證只有一筆成功
            throw new ConflictException("The time slot has already been booked.", exception);
        }
    }

    // 2601：違反唯一索引；2627：違反 UNIQUE / PRIMARY KEY 條件約束
    private static bool IsUniqueIndexViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
