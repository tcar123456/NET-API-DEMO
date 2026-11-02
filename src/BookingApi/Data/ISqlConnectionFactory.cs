using System.Data.Common;

namespace BookingApi.Data;

// 提供給 Dapper 使用的資料庫連線；EF Core 自己管理連線，不需要經過這裡
public interface ISqlConnectionFactory
{
    DbConnection CreateConnection();
}
