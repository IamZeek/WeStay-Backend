using Microsoft.EntityFrameworkCore;
using WeStay.NotificationService.Data;
using WeStay.NotificationService.Repositories;
using WeStay.NotificationService.Repositories.Interfaces;
using WeStay.NotificationService.Services;
using WeStay.NotificationService.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WeStay.NotificationService.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("NotificationConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'NotificationConnection' is not configured. " +
        "Set it via User Secrets or an environment variable (it must not be committed to appsettings.json).");
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Configure Twilio Settings
builder.Services.Configure<TwilioSettings>(builder.Configuration.GetSection("TwilioSettings"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.Configure<PushNotificationSettings>(builder.Configuration.GetSection("PushNotificationSettings"));

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
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationTypeRepository, NotificationTypeRepository>();
builder.Services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
builder.Services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();

// Register services
builder.Services.AddScoped<INotificationServices, NotificationServices>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISMSService, SMSService>(); // Register SMS service
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "WeStay Notification Service API",
        Version = "v1",
        Description = "Microservice for managing notifications in WeStay application"
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

// Add HttpClient for communicating with other services
builder.Services.AddHttpClient();

// Add hosted service for processing notifications in background
builder.Services.AddHostedService<NotificationProcessorService>();

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

// Schema is managed by EF Core migrations (apply with `dotnet ef database update`),
// matching AuthService/ListingService. EnsureCreated() was removed.
app.Run();