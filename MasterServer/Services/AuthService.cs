using MySql.Data.MySqlClient;
using Dapper;
using BCrypt.Net;

public class AuthService
{
    private readonly string _connectionString;
    private readonly JwtHelper _jwtHelper;

    public AuthService(IConfiguration configuration, JwtHelper jwtHelper)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _jwtHelper = jwtHelper;
    }

    public bool Register(RegisterRequest req)
    {
        using var conn = new MySqlConnection(_connectionString);

        var existing = conn.QueryFirstOrDefault<User>(
            "SELECT * FROM Users WHERE username = @username",
            new { req.username });

        if (existing != null) return false;

        string hash = BCrypt.Net.BCrypt.HashPassword(req.password);

        conn.Execute(
            "INSERT INTO Users(username, password_hash, email) VALUES(@username, @password, @email)",
            new
            {
                username = req.username,
                password = hash,
                email = req.email
            });

        return true;
    }

    public (User, string) Login(LoginRequest req)
    {
        using var conn = new MySqlConnection(_connectionString);

        var user = conn.QueryFirstOrDefault<User>(
            "SELECT * FROM Users WHERE username = @username",
            new { req.username });

        if (user == null) return (null, null);

        bool valid = BCrypt.Net.BCrypt.Verify(req.password, user.password_hash);

        if (!valid) return (null, null);

        var jwt = _jwtHelper.GenerateToken(user);

        return (user, jwt);
    }
}