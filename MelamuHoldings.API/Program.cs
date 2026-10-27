using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MelamuHoldings.API.Cli;
using MelamuHoldings.Application.Interfaces;
using MelamuHoldings.Application.Services;
using MelamuHoldings.Infrastructure.Data;
using MelamuHoldings.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Controllers, errors & Swagger
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database — the connection string comes from appsettings.Development.json, user-secrets or ConnectionStrings__DefaultConnection
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
    options.UseSqlServer(connectionString);
});

// JWT Authentication — the signing key must come from user-secrets or the Jwt__Key environment variable
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => o.IsValid(),
        $"Jwt settings are incomplete. Jwt:Key must be at least {JwtOptions.MinimumKeyBytes} bytes " +
        "(set it with `dotnet user-secrets set Jwt:Key <value>` or the Jwt__Key environment variable).")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Key))
        };
    });

builder.Services.AddAuthorization();

// Login throttling
var loginAttemptsPerMinute = builder.Configuration.GetValue("Auth:LoginAttemptsPerMinute", 5);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginAttemptsPerMinute,
            Window = TimeSpan.FromMinutes(1)
        }));
});

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITradeRepository, TradeRepository>();
builder.Services.AddScoped<IStrategyRepository, StrategyRepository>();
builder.Services.AddScoped<IUploadLogRepository, UploadLogRepository>();

// Services
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OwnerAccountService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<TradeService>();
builder.Services.AddScoped<UploadService>();
builder.Services.AddScoped<CsvParserService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<BehaviourAnalyticsService>();

var app = builder.Build();

// One-off operator commands: `dotnet run -- create-owner`
if (args.FirstOrDefault() == CreateOwnerCommand.Name)
    return await CreateOwnerCommand.RunAsync(app.Services);

// Middleware Pipeline
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
return 0;

// Exposed for WebApplicationFactory in the integration tests
public partial class Program;
