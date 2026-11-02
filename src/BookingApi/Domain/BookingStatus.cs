namespace BookingApi.Domain;

// 底層型別指定為 byte，對應資料庫的 tinyint
public enum BookingStatus : byte
{
    Active = 1,
    Cancelled = 2
}
