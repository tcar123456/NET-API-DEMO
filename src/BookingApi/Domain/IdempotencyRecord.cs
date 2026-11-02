namespace BookingApi.Domain;

// 記錄某個 Idempotency-Key 第一次請求的內容與結果，讓重送的請求回傳同樣的結果而不重複建立
public class IdempotencyRecord
{
    public required string Key { get; set; }

    // 請求內容的 SHA-256（十六進位 64 字元），同一個 Key 送來不同內容時用來拒絕
    public required string RequestHash { get; set; }

    // 處理中為 null；完成後記錄第一次回應的狀態碼與內容
    public int? StatusCode { get; set; }

    public string? ResponseBody { get; set; }

    public DateTime CreatedAt { get; set; }
}
