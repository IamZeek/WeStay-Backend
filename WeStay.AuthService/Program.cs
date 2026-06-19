using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WeStay.AuthService.Data;
using WeStay.AuthService.Models;
using WeStay.AuthService.Services;
using WeStay.AuthService.Services.Interfaces;
using WeStay.AuthService.Utilities;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured. " +
        "Set it via User Secrets or an environment variable (it must not be committed to appsettings.json).");
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IExternalAuthService, ExternalAuthService>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IVerificationService, VerificationService>();
builder.Services.AddScoped<IPhoneVerificationService, PhoneVerificationService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Thin client that delegates event-driven Email/SMS to NotificationService over HTTP.
// Short timeout so a slow/unreachable NotificationService can never stall registration.
builder.Services.AddHttpClient<NotificationClient>(c => c.Timeout = TimeSpan.FromSeconds(5));


// Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "JWT signing key 'Jwt:Key' is not configured or is shorter than 32 characters. " +
        "Set it via User Secrets (dotnet user-secrets set \"Jwt:Key\" \"<key>\") or an environment variable. " +
        "It must not be committed to appsettings.json.");
}

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "WeStay",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "WeStayUsers",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

// Google OAuth is registered ONLY when its credentials are configured.
// Google is a remote (IAuthenticationRequestHandler) scheme: app.UseAuthentication() initializes
// every such scheme on EVERY request (to let it handle its callback path), and that initialization
// runs OAuthOptions.Validate(). So if Google is registered without a ClientId/ClientSecret, its
// validation throws on every request and returns 500 for ALL endpoints (including
// /api/auth/register), not just OAuth ones. Gating registration on config presence keeps Google
// available (it lights up automatically once the secrets are set) without breaking the rest of the
// API when it isn't configured yet.
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SaveTokens = true;
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.ClaimActions.MapJsonKey("picture", "picture");
    });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireGuestRole", policy => policy.RequireRole("Guest"));
    options.AddPolicy("RequireHostRole", policy => policy.RequireRole("Host", "Guest"));
});

builder.Services.AddMemoryCache();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Seed a configurable admin user (idempotent). In Development a default password is used so the
// app + integration tests have an admin out of the box; in other environments the admin is only
// seeded when AdminSeed:Password is explicitly configured — never seed a default-password admin
// in production. Override AdminSeed:Email / AdminSeed:Password via User Secrets or env vars.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var adminEmail = app.Configuration["AdminSeed:Email"] ?? "admin@westay.local";
    var adminPassword = app.Configuration["AdminSeed:Password"];
    if (string.IsNullOrEmpty(adminPassword) && app.Environment.IsDevelopment())
    {
        adminPassword = "Admin123!"; // dev-only default
    }

    if (!string.IsNullOrEmpty(adminPassword))
    {
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
        if (admin == null)
        {
            db.Users.Add(new User
            {
                Email = adminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                FirstName = "System",
                LastName = "Admin",
                PhoneNumber = "+10000000000",
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            app.Logger.LogInformation("Seeded admin user {Email}", adminEmail);
        }
        else if (admin.Role != UserRole.Admin)
        {
            admin.Role = UserRole.Admin;
            admin.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            app.Logger.LogInformation("Promoted existing user {Email} to Admin", adminEmail);
        }
    }
}

app.Run();