namespace BookingApi.Exceptions;

// 同一個 Idempotency-Key 送來不同的請求內容（回 422）
public class IdempotencyKeyReusedException : Exception
{
    public IdempotencyKeyReusedException(string message)
        : base(message)
    {
    }
}
