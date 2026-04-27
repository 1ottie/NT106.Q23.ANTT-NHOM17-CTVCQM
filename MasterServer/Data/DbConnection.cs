using MySql.Data.MySqlClient;
using System.Data;

public class DbConnection
{
    private readonly string _connectionString;

    public DbConnection(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection");
    }

    public IDbConnection GetConnection()
    {
        return new MySqlConnection(_connectionString);
    }
}