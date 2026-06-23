using Microsoft.EntityFrameworkCore;
using WeStay.BookingService.Data;
using WeStay.BookingService.Repositories;
using WeStay.BookingService.Repositories.Interfaces;
using WeStay.BookingService.Services;
using WeStay.BookingService.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("BookingConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'BookingConnection' is not configured. " +
        "Set it via User Secrets or an environment variable (it must not be committed to appsettings.json).");
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "JWT signing key 'Jwt:Key' is not configured or is shorter than 32 characters. " +
        "Set it via User Secrets (dotnet user-secrets set \"Jwt:Key\" \"<key>\") or an environment variable. " +
        "It must not be committed to appsettings.json.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

// Register repositories
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IBookingStatusRepository, BookingStatusRepository>();
builder.Services.AddScoped<IBookingPaymentRepository, BookingPaymentRepository>();
builder.Services.AddScoped<IPlatformFeeConfigRepository, PlatformFeeConfigRepository>();
// IBookingReviewRepository moved to /Future (Phase 3 — Reviews); not registered.

// Register services
builder.Services.AddScoped<IBookingService,BookingService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
// Delegates Email/SMS for booking events to NotificationService over HTTP (best-effort).
// Short timeout so a slow/unreachable NotificationService can never stall a booking operation.
// Sends the shared internal service key (NotificationService's /email + /sms require it).
builder.Services.AddHttpClient<NotificationClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
    var internalKey = builder.Configuration["ServiceAuth:InternalApiKey"];
    if (!string.IsNullOrEmpty(internalKey)) c.DefaultRequestHeaders.Add("X-Internal-Api-Key", internalKey);
});

// Background jobs for automatic booking state transitions (intervals/window in the "Booking" config).
builder.Services.AddHostedService<BookingCompletionService>();
builder.Services.AddHostedService<BookingExpiryService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "WeStay Booking Service API",
        Version = "v1",
        Description = "Microservice for managing bookings and reservations in WeStay application"
    });

    // Add JWT Bearer token support in Swagger
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your valid token in the text input below."
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add HttpClient for communicating with other services (price/capacity/owner on ListingService,
// contact on AuthService — all protected internal endpoints, so send the shared service key).
builder.Services.AddHttpClient(string.Empty, c =>
{
    var internalKey = builder.Configuration["ServiceAuth:InternalApiKey"];
    if (!string.IsNullOrEmpty(internalKey)) c.DefaultRequestHeaders.Add("X-Internal-Api-Key", internalKey);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    context.Database.EnsureCreated();
}

app.Run();