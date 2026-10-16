using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwt = AddJwtAuthentication(builder);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:5174"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddDbContext<AuthDb>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Saloon")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Saloon.")));

var app = builder.Build();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "auth", status = "healthy" }));
app.MapPost("/api/auth/login", async (LoginRequest request, AuthDb db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 50
        || string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
        return Results.BadRequest(new { message = "Username or password is invalid." });

    var username = request.Username.Trim().ToLowerInvariant();
    var user = await db.Users.AsNoTracking()
        .SingleOrDefaultAsync(u => u.Username == username && u.IsActive);
    var passwordMatches = user is null
        ? PasswordMatchesDummy(request.Password)
        : PasswordMatches(request.Password, user);
    if (user is null || !passwordMatches)
        return Results.Unauthorized();

    return Results.Ok(CreateAuthenticatedUser(user, jwt));
}).RequireRateLimiting("login");

app.MapPost("/api/auth/register", async (RegisterRequest request, AuthDb db) =>
{
    var username = request.Username?.Trim().ToLowerInvariant();
    var displayName = request.DisplayName?.Trim();
    if (string.IsNullOrWhiteSpace(username) || username.Length is < 3 or > 50
        || !username.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
        || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120)
        return Results.BadRequest(new { message = "Use a valid username and display name." });
    if (string.IsNullOrEmpty(request.Password) || request.Password.Length is < 12 or > 128
        || !request.Password.Any(char.IsLetter) || !request.Password.Any(char.IsDigit))
        return Results.BadRequest(new { message = "Password must be 12–128 characters and include letters and numbers." });
    if (await db.Users.AnyAsync(user => user.Username == username))
        return Results.Conflict(new { message = "That username is already in use." });

    var salt = RandomNumberGenerator.GetBytes(16);
    var newUser = new User
    {
        Username = username,
        DisplayName = displayName,
        Role = "Customer",
        PasswordSalt = salt,
        PasswordIterations = 210_000,
        PasswordHash = Rfc2898DeriveBytes.Pbkdf2(request.Password, salt, 210_000,
            HashAlgorithmName.SHA256, 32)
    };
    db.Users.Add(newUser);
    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
    {
        return Results.Conflict(new { message = "That username is already in use." });
    }
    return Results.Ok(CreateAuthenticatedUser(newUser, jwt));
}).RequireRateLimiting("register");

app.Run();

static JwtSettings AddJwtAuthentication(WebApplicationBuilder builder)
{
    var section = builder.Configuration.GetSection("Jwt");
    var keyText = section["SigningKey"];
    if (string.IsNullOrWhiteSpace(keyText) || Encoding.UTF8.GetByteCount(keyText) < 32)
        throw new InvalidOperationException("Set Jwt__SigningKey to a secret of at least 32 UTF-8 bytes.");

    var settings = new JwtSettings(
        section["Issuer"] ?? "Saloon.Auth",
        section["Audience"] ?? "Saloon.Api",
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText)),
        int.TryParse(section["LifetimeMinutes"], out var minutes) ? Math.Clamp(minutes, 5, 120) : 60);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = settings.SigningKey,
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
                RoleClaimType = "role"
            };
        });
    builder.Services.AddAuthorization();
    return settings;
}

static bool PasswordMatches(string password, User user)
{
    var candidate = Rfc2898DeriveBytes.Pbkdf2(password, user.PasswordSalt, user.PasswordIterations,
        HashAlgorithmName.SHA256, user.PasswordHash.Length);
    return CryptographicOperations.FixedTimeEquals(candidate, user.PasswordHash);
}

static AuthenticatedUser CreateAuthenticatedUser(User user, JwtSettings jwt)
{
    var expiresAt = DateTime.UtcNow.AddMinutes(jwt.LifetimeMinutes);
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim("username", user.Username),
        new Claim("name", user.DisplayName),
        new Claim("role", user.Role)
    };
    var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims,
        notBefore: DateTime.UtcNow,
        expires: expiresAt,
        signingCredentials: new SigningCredentials(jwt.SigningKey, SecurityAlgorithms.HmacSha256));
    return new AuthenticatedUser(user.Id, user.Username, user.DisplayName,
        user.Role, new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
}

static bool PasswordMatchesDummy(string password)
{
    var salt = new byte[16];
    var expected = new byte[32];
    var candidate = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
    return CryptographicOperations.FixedTimeEquals(candidate, expected);
}

record LoginRequest(string Username, string Password);
record RegisterRequest(string Username, string DisplayName, string Password);
record AuthenticatedUser(int Id, string Username, string Name, string Role,
    string AccessToken, DateTime ExpiresAtUtc);
record JwtSettings(string Issuer, string Audience, SymmetricSecurityKey SigningKey, int LifetimeMinutes);

class AuthDb(DbContextOptions<AuthDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().ToTable("Users");
        modelBuilder.Entity<User>().HasIndex(user => user.Username).IsUnique();
        modelBuilder.Entity<User>().Property(user => user.Username).HasMaxLength(50);
        modelBuilder.Entity<User>().Property(user => user.DisplayName).HasMaxLength(120);
        modelBuilder.Entity<User>().Property(user => user.Role).HasMaxLength(20);
    }
}

class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "Customer";
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] PasswordHash { get; set; } = [];
    public int PasswordIterations { get; set; } = 210000;
    public bool IsActive { get; set; } = true;
}
