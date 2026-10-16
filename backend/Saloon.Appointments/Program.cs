using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
ConfigureJwt(builder);
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:5174"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddDbContext<BookingDb>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Saloon")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Saloon.")));

var app = builder.Build();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "appointments", status = "healthy" }));

app.MapGet("/api/appointments", async (DateOnly? date, BookingDb db) =>
{
    var appointments = db.Appointments.AsNoTracking();
    if (date is not null)
    {
        var startOfDay = date.Value.ToDateTime(TimeOnly.MinValue);
        var endOfDay = startOfDay.AddDays(1);
        appointments = appointments.Where(appointment => appointment.StartAt >= startOfDay
            && appointment.StartAt < endOfDay);
    }
    return Results.Ok(await appointments.OrderBy(appointment => appointment.StartAt).ToListAsync());
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapGet("/api/appointments/slots", async (DateOnly date, int serviceId, BookingDb db) =>
{
    var service = await db.Services.FindAsync(serviceId);
    if (service is null || !service.IsActive) return Results.NotFound();
    var start = date.ToDateTime(new TimeOnly(9, 0));
    var end = date.ToDateTime(new TimeOnly(18, 0));
    var now = DateTime.Now;
    var busy = await db.Appointments.Where(appointment => appointment.StartAt.Date == start.Date
        && appointment.Status != "Cancelled")
        .Select(appointment => new { appointment.StartAt, appointment.DurationMinutes }).ToListAsync();
    var slots = new List<string>();
    for (var time = start; time.AddMinutes(service.DurationMinutes) <= end; time = time.AddMinutes(30))
    {
        var overlaps = busy.Any(appointment => time < appointment.StartAt.AddMinutes(appointment.DurationMinutes)
            && time.AddMinutes(service.DurationMinutes) > appointment.StartAt);
        if ((date != DateOnly.FromDateTime(now) || time > now) && !overlaps)
            slots.Add(time.ToString("HH:mm"));
    }
    return Results.Ok(slots);
}).AllowAnonymous();

app.MapPost("/api/appointments", async (CreateAppointment request, ClaimsPrincipal principal, BookingDb db) =>
{
    var isCustomer = principal.IsInRole("Customer");
    var customerName = isCustomer ? principal.FindFirstValue("name") : request.CustomerName?.Trim();
    if (string.IsNullOrWhiteSpace(customerName))
        return Results.BadRequest("Customer name is required.");
    var service = await db.Services.FindAsync(request.ServiceId);
    if (service is null || !service.IsActive) return Results.BadRequest("Service is unavailable.");
    if (request.StartAt < DateTime.Now || request.StartAt.TimeOfDay < new TimeSpan(9, 0, 0)
        || request.StartAt.AddMinutes(service.DurationMinutes).TimeOfDay > new TimeSpan(18, 0, 0))
        return Results.BadRequest("Choose a future time between 9 AM and 6 PM.");
    if (await db.Appointments.AnyAsync(appointment => appointment.Status != "Cancelled"
        && appointment.StartAt < request.StartAt.AddMinutes(service.DurationMinutes)
        && appointment.StartAt.AddMinutes(appointment.DurationMinutes) > request.StartAt))
        return Results.Conflict("That time was just booked.");

    var appointment = new Appointment
    {
        CustomerName = customerName,
        Phone = request.Phone,
        ServiceId = service.Id,
        ServiceName = service.Name,
        Price = service.Price,
        DurationMinutes = service.DurationMinutes,
        StartAt = request.StartAt,
        Status = "Confirmed"
    };
    db.Appointments.Add(appointment);
    await db.SaveChangesAsync();
    return Results.Created($"/api/appointments/{appointment.Id}", appointment);
}).RequireAuthorization(policy => policy.RequireRole("Customer", "Admin"));

app.MapPut("/api/appointments/{id:int}", async (int id, EditAppointment request, BookingDb db) =>
{
    var appointment = await db.Appointments.FindAsync(id);
    if (appointment is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(request.CustomerName)) return Results.BadRequest("Customer name is required.");
    var service = await db.Services.FindAsync(request.ServiceId);
    if (service is null || !service.IsActive) return Results.BadRequest("Service is unavailable.");
    if (request.StartAt < DateTime.Now || request.StartAt.TimeOfDay < new TimeSpan(9, 0, 0)
        || request.StartAt.AddMinutes(service.DurationMinutes).TimeOfDay > new TimeSpan(18, 0, 0))
        return Results.BadRequest("Choose a future time between 9 AM and 6 PM.");
    if (await db.Appointments.AnyAsync(other => other.Id != id && other.Status != "Cancelled"
        && other.StartAt < request.StartAt.AddMinutes(service.DurationMinutes)
        && other.StartAt.AddMinutes(other.DurationMinutes) > request.StartAt))
        return Results.Conflict("That time overlaps another appointment.");

    appointment.CustomerName = request.CustomerName.Trim();
    appointment.Phone = request.Phone;
    appointment.ServiceId = service.Id;
    appointment.ServiceName = service.Name;
    appointment.Price = service.Price;
    appointment.DurationMinutes = service.DurationMinutes;
    appointment.StartAt = request.StartAt;
    await db.SaveChangesAsync();
    return Results.Ok(appointment);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapDelete("/api/appointments/{id:int}", async (int id, BookingDb db) =>
{
    var appointment = await db.Appointments.FindAsync(id);
    if (appointment is null) return Results.NotFound();
    db.Appointments.Remove(appointment);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapPatch("/api/appointments/{id:int}/status", async (int id, UpdateStatus request, BookingDb db) =>
{
    var appointment = await db.Appointments.FindAsync(id);
    if (appointment is null) return Results.NotFound();
    if (!new[] { "Confirmed", "Checked in", "Completed", "Cancelled", "No show" }.Contains(request.Status))
        return Results.BadRequest("Invalid status.");
    appointment.Status = request.Status;
    await db.SaveChangesAsync();
    return Results.Ok(appointment);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.Run();

static void ConfigureJwt(WebApplicationBuilder builder)
{
    var section = builder.Configuration.GetSection("Jwt");
    var keyText = section["SigningKey"];
    if (string.IsNullOrWhiteSpace(keyText) || Encoding.UTF8.GetByteCount(keyText) < 32)
        throw new InvalidOperationException("Set Jwt__SigningKey to a secret of at least 32 UTF-8 bytes.");
    var issuer = section["Issuer"] ?? "Saloon.Auth";
    var audience = section["Audience"] ?? "Saloon.Api";
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
                RoleClaimType = "role"
            };
        });
    builder.Services.AddAuthorization();
}

record CreateAppointment(string? CustomerName, string? Phone, int ServiceId, DateTime StartAt);
record EditAppointment(string CustomerName, string? Phone, int ServiceId, DateTime StartAt);
record UpdateStatus(string Status);

class BookingDb(DbContextOptions<BookingDb> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Service> Services => Set<Service>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>().Property(appointment => appointment.Price).HasColumnType("decimal(10,2)");
        modelBuilder.Entity<Appointment>().HasIndex(appointment => appointment.StartAt);
        modelBuilder.Entity<Service>().Property(service => service.Price).HasColumnType("decimal(10,2)");
    }
}

class Appointment
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string? Phone { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = "";
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
    public DateTime StartAt { get; set; }
    public string Status { get; set; } = "Confirmed";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

class Service
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
