using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwt = ConfigureJwt(builder);
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:5174"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<CatalogDb>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Saloon")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Saloon.")));

var app = builder.Build();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "catalog", status = "healthy" }));
app.MapGet("/api/services", async (CatalogDb db) =>
    Results.Ok(await db.Services.Where(service => service.IsActive).OrderBy(service => service.Name).ToListAsync()));
app.MapPost("/api/services", async (SaveService request, CatalogDb db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || request.Price < 0 || request.DurationMinutes < 5)
        return Results.BadRequest("Name, non-negative price, and duration of at least 5 minutes are required.");
    var service = new Service
    {
        Name = request.Name.Trim(),
        Category = request.Category,
        DurationMinutes = request.DurationMinutes,
        Price = request.Price,
        IsActive = true
    };
    db.Services.Add(service);
    await db.SaveChangesAsync();
    return Results.Created($"/api/services/{service.Id}", service);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPut("/api/services/{id:int}", async (int id, SaveService request, CatalogDb db) =>
{
    var service = await db.Services.FindAsync(id);
    if (service is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.Name) || request.Price < 0 || request.DurationMinutes < 5)
        return Results.BadRequest();
    service.Name = request.Name.Trim();
    service.Category = request.Category;
    service.DurationMinutes = request.DurationMinutes;
    service.Price = request.Price;
    await db.SaveChangesAsync();
    return Results.Ok(service);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapDelete("/api/services/{id:int}", async (int id, CatalogDb db) =>
{
    var service = await db.Services.FindAsync(id);
    if (service is null) return Results.NotFound();
    service.IsActive = false;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.Run();

static JwtSecurityOptions ConfigureJwt(WebApplicationBuilder builder)
{
    var section = builder.Configuration.GetSection("Jwt");
    var keyText = section["SigningKey"];
    if (string.IsNullOrWhiteSpace(keyText) || Encoding.UTF8.GetByteCount(keyText) < 32)
        throw new InvalidOperationException("Set Jwt__SigningKey to a secret of at least 32 UTF-8 bytes.");
    var settings = new JwtSecurityOptions(section["Issuer"] ?? "Saloon.Auth",
        section["Audience"] ?? "Saloon.Api",
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText)));
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

record SaveService(string Name, string Category, int DurationMinutes, decimal Price);
record JwtSecurityOptions(string Issuer, string Audience, SymmetricSecurityKey SigningKey);

class CatalogDb(DbContextOptions<CatalogDb> options) : DbContext(options)
{
    public DbSet<Service> Services => Set<Service>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Service>().Property(service => service.Price).HasColumnType("decimal(10,2)");
        modelBuilder.Entity<Service>().HasData(
            new Service { Id = 1, Name = "Haircut & blow dry", Category = "Hair", DurationMinutes = 60, Price = 500 },
            new Service { Id = 2, Name = "Head massage", Category = "Wellness", DurationMinutes = 45, Price = 600 },
            new Service { Id = 3, Name = "Hair coloring", Category = "Hair", DurationMinutes = 120, Price = 7500 },
            new Service { Id = 4, Name = "Manicure", Category = "Nails", DurationMinutes = 60, Price = 2200 },
            new Service { Id = 5, Name = "Beard trim", Category = "Grooming", DurationMinutes = 30, Price = 1200 });
    }
}

class Service
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
