using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

Console.OutputEncoding = System.Text.Encoding.UTF8;

// JWT
var jwtKey = builder.Configuration["Jwt:Secret"] ?? "YourSuperSecretKeyHere1234567890";

// SERVICES
builder.Services.AddControllers();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SERVICES CUSTOM
builder.Services.AddScoped<JwtHelper>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<NodeService>();
builder.Services.AddScoped<DbConnection>();
builder.Services.AddScoped<RoomService>();

// KESTREL LAN
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5274);
});

var app = builder.Build();

// SWAGGER
app.UseSwagger();
app.UseSwaggerUI();

// AUTH
app.UseAuthentication();
app.UseAuthorization();

// CONTROLLERS
app.MapControllers();

app.Run();