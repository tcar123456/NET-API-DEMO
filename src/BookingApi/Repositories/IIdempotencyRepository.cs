using BookingApi.Domain;

namespace BookingApi.Repositories;

public interface IIdempotencyRepository
{
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken);

    // 以主鍵搶下這個 Key；已經有人用過（或正在用）時回傳 false
    Task<bool> TryReserveAsync(IdempotencyRecord record, CancellationToken cancellationToken);

    Task CompleteAsync(string key, int statusCode, string responseBody, CancellationToken cancellationToken);

    // 處理失敗時釋放 Key，讓用戶端可以用同一個 Key 重試
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}
