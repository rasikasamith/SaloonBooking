using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Host == "localhost" || uri.Host == "127.0.0.1")
        && (uri.Port == 5173 || uri.Port == 5174))
        .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<AuthDb>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Saloon")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Saloon.")));

var app = builder.Build();
app.UseCors();
app.MapGet("/health", () => Results.Ok(new { service = "auth", status = "healthy" }));
app.MapPost("/api/auth/login", async (LoginRequest request, AuthDb db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        return Results.BadRequest(new { message = "Username and password are required." });

    var username = request.Username.Trim().ToLowerInvariant();
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username && u.IsActive);
    if (user is null || !PasswordMatches(request.Password, user))
        return Results.Unauthorized();

    return Results.Ok(new AuthenticatedUser(user.Id, user.Username, user.DisplayName, user.Role));
});
app.Run();

static bool PasswordMatches(string password, User user)
{
    var candidate = Rfc2898DeriveBytes.Pbkdf2(password, user.PasswordSalt, user.PasswordIterations,
        HashAlgorithmName.SHA256, user.PasswordHash.Length);
    return CryptographicOperations.FixedTimeEquals(candidate, user.PasswordHash);
}

record LoginRequest(string Username, string Password);
record AuthenticatedUser(int Id, string Username, string Name, string Role);

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
