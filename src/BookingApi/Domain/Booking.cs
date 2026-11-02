namespace BookingApi.Domain;

public class Booking
{
    public int Id { get; set; }

    public int ProviderId { get; set; }

    public int ServiceId { get; set; }

    public int CustomerId { get; set; }

    // 台灣時間（UTC+8），不含時區資訊，對應 datetime2
    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Active;

    public DateTime CreatedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    // 由 SQL Server 在每次更新時自動產生，EF Core 用來偵測並發修改
    public byte[] RowVersion { get; set; } = [];

    // 導覽屬性由 EF Core 在查詢時（Include）填入，不是由程式碼建構時設定。
    // 宣告為非 null 是因為資料庫的外鍵保證關聯一定存在；
    // 用 null! 告訴編譯器「初始化交給 EF Core」，否則 nullable 檢查會警告未初始化。
    public Provider Provider { get; set; } = null!;

    public Service Service { get; set; } = null!;

    public Customer Customer { get; set; } = null!;
}
