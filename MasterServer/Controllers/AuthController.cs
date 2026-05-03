using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest req)
    {
        var result = _authService.Register(req);

        if (!result)
            return BadRequest(new { message = "Username already exists" });

        return Ok(new { message = "Register success" });
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        var (user, token) = _authService.Login(req);

        if (user == null)
            return Unauthorized(new { message = "Invalid credentials" });

        return Ok(new
        {
            token = token,
            user = new
            {
                user.user_id,
                user.username,
                user.email
            }
        });
    }
    [HttpGet("test-db")]
    public IActionResult TestDb([FromServices] DbConnection dbConfig)
    {
        try
        {
            using var conn = dbConfig.GetConnection();
            conn.Open(); // Thực hiện kết nối
            return Ok(new { message = "Kết nối Database thành công!", status = "OK" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi kết nối DB", error = ex.Message });
        }
    }
}