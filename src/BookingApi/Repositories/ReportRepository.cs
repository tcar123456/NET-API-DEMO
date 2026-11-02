using System.Data;
using BookingApi.Data;
using BookingApi.Domain;
using Dapper;

namespace BookingApi.Repositories;

// 報表用 Dapper + 手寫 T-SQL：唯讀的彙總查詢，需要完全掌握 JOIN、GROUP BY 與條件彙總的寫法，
// 也方便把同一段 SQL 貼到 SSMS 看執行計畫
public class ReportRepository : IReportRepository
{
    // LEFT JOIN：當月沒有預約的師傅也要出現（預約數 0、營收 0）。
    // 期間條件放在 JOIN 的 ON 而不是 WHERE，否則沒有預約的師傅會被 WHERE 過濾掉。
    // 停用的師傅只在當月有預約時才列出。
    private const string ProviderRevenueSql = """
        SELECT
            p.Id   AS ProviderId,
            p.Name AS ProviderName,
            COUNT(CASE WHEN b.Status = @Active THEN 1 END)    AS BookingCount,
            COUNT(CASE WHEN b.Status = @Cancelled THEN 1 END) AS CancelledCount,
            COALESCE(SUM(CASE WHEN b.Status = @Active THEN s.Price END), 0) AS Revenue
        FROM dbo.Providers AS p
        LEFT JOIN dbo.Bookings AS b
            ON b.ProviderId = p.Id
           AND b.StartTime >= @From
           AND b.StartTime < @To
        LEFT JOIN dbo.Services AS s
            ON s.Id = b.ServiceId
        WHERE p.IsActive = 1 OR b.Id IS NOT NULL
        GROUP BY p.Id, p.Name
        ORDER BY Revenue DESC, p.Id;
        """;

    private readonly ISqlConnectionFactory _connectionFactory;

    public ReportRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProviderMonthlyRevenue>> GetProviderRevenueAsync(
        DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        // Dapper 預設把 DateTime 當成 SQL 的 datetime；欄位是 datetime2，明確指定型別避免隱含轉換與精度不一致
        parameters.Add("From", from, DbType.DateTime2);
        parameters.Add("To", to, DbType.DateTime2);
        parameters.Add("Active", (byte)BookingStatus.Active, DbType.Byte);
        parameters.Add("Cancelled", (byte)BookingStatus.Cancelled, DbType.Byte);

        await using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProviderMonthlyRevenue>(
            new CommandDefinition(ProviderRevenueSql, parameters, cancellationToken: cancellationToken));

        return rows.ToList();
    }
}
