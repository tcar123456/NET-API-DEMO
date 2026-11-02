using BookingApi.Data;
using BookingApi.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Repositories;

public class IdempotencyRepository : IIdempotencyRepository
{
    private readonly AppDbContext _db;

    public IdempotencyRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken)
    {
        return _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key, cancellationToken);
    }

    public async Task<bool> TryReserveAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        _db.IdempotencyRecords.Add(record);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2627 })
        {
            // 主鍵衝突：同一個 Key 已經存在。
            // 失敗的實體仍被 DbContext 追蹤，必須移除，否則之後建立預約的 SaveChanges 會再嘗試寫入它
            _db.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    public Task CompleteAsync(string key, int statusCode, string responseBody, CancellationToken cancellationToken)
    {
        // ExecuteUpdateAsync 直接送出 UPDATE，不需要先把資料讀進 DbContext
        return _db.IdempotencyRecords
            .Where(r => r.Key == key)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.StatusCode, statusCode)
                .SetProperty(r => r.ResponseBody, responseBody),
                cancellationToken);
    }

    public Task ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        return _db.IdempotencyRecords
            .Where(r => r.Key == key && r.StatusCode == null)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
