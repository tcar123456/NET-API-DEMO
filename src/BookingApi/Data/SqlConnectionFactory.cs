using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace BookingApi.Data;

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    // 每次回傳新的連線物件，由呼叫端 using 釋放；實體連線由 SqlClient 的連線池重複使用
    public DbConnection CreateConnection() => new SqlConnection(_connectionString);
}
