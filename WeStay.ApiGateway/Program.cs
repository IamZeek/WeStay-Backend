using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Text;
using WeStay.ApiGateway.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["Secret"];
if (string.IsNullOrEmpty(secretKey) || secretKey.Length < 32)
{
    throw new InvalidOperationException(
        "JWT signing key 'JwtSettings:Secret' is not configured or is shorter than 32 characters. " +
        "Set it via User Secrets (dotnet user-secrets set \"JwtSettings:Secret\" \"<key>\") or an environment variable. " +
        "It must not be committed to appsettings.json.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer("Bearer", options =>
{
    // Keep JWT claim names as-is (don't remap "role" to the long ClaimTypes.Role URI) so both
    // the role policies below and Ocelot's per-route RouteClaimsRequirement can match on "role".
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero,
        // The token carries the role under the short "role" claim (AuthService issues
        // ClaimTypes.Role, which serializes to "role"). Point the role/name claim types at the
        // short names so RequireRole(...) in the AdminOnly/HostOnly policies works.
        RoleClaimType = "role",
        NameClaimType = "nameid"
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine("Token validated successfully");
            return Task.CompletedTask;
        }
    };
});

// Add Authorization with policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser());

    // These policies now resolve correctly because the role claim ("role") is issued by
    // AuthService and RoleClaimType is set above. They apply to any controller hosted in the
    // gateway; Ocelot-PROXIED routes enforce roles via "RouteClaimsRequirement" in ocelot.json
    // (single-role checks, e.g. Admin). Host-or-Admin endpoints are enforced at the owning
    // service via [Authorize(Roles="Host,Admin")] since RouteClaimsRequirement can't express OR.
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));

    options.AddPolicy("HostOnly", policy =>
        policy.RequireRole("Host", "Admin"));

    options.AddPolicy("GuestOnly", policy =>
        policy.RequireRole("Guest", "Admin"));
});

// Add Ocelot with configuration
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
builder.Services.AddOcelot(builder.Configuration);

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("WeStayCorsPolicy", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        var allowedMethods = builder.Configuration.GetSection("Cors:AllowedMethods").Get<string[]>();
        var allowedHeaders = builder.Configuration.GetSection("Cors:AllowedHeaders").Get<string[]>();

        policy.WithOrigins(allowedOrigins ?? new[] { "http://localhost:3000" })
              .WithMethods(allowedMethods ?? new[] { "GET", "POST", "PUT", "DELETE", "OPTIONS" })
              .WithHeaders(allowedHeaders ?? new[] { "Content-Type", "Authorization" })
              .AllowCredentials();
    });
});

// Add Health Checks
builder.Services.AddHealthChecks();

// Add custom middleware
// NOTE: AuthenticationMiddleware was removed. It never enforced anything because Ocelot-routed
// requests carry no MVC endpoint metadata, so GetEndpoint() returned null and the auth check was
// skipped. JWT validation is handled by Ocelot's per-route "AuthenticationOptions" (the "Bearer"
// scheme configured above), which is the idiomatic Ocelot approach.

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("WeStayCorsPolicy");

app.UseAuthentication();
app.UseAuthorization();

// Use custom middleware
app.UseMiddleware<LoggingMiddleware>();

app.UseHealthChecks("/health");

await app.UseOcelot();

app.MapControllers();

app.Run();