using Microsoft.EntityFrameworkCore;
using WeStay.MessagingService.Data;
using WeStay.MessagingService.Repositories;
using WeStay.MessagingService.Repositories.Interfaces;
using WeStay.MessagingService.Services;
using WeStay.MessagingService.Services.Interfaces;
using WeStay.MessagingService.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.Extensions.FileProviders;
using WeStay.MessagingService.Settings;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("MessagingConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'MessagingConnection' is not configured. " +
        "Set it via User Secrets or an environment variable (it must not be committed to appsettings.json).");
builder.Services.AddDbContext<MessagingDbContext>(options =>
    options.UseSqlServer(connectionString));

// Add SignalR for real-time messaging
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 1024000; // 1MB
});

//SMS and mail Setting
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.Configure<SmsSettings>(builder.Configuration.GetSection("SmsSettings"));

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

        // Configure JWT Bearer for SignalR
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/messagehub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// Register repositories
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<IConversationTypeRepository, ConversationTypeRepository>();

// Register services
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<INotificationServices, NotificationServices>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "WeStay Messaging Service API",
        Version = "v1",
        Description = "Microservice for managing messaging and real-time communication in WeStay application"
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
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials(); // Important for SignalR
    });
});

// Add HttpClient for communicating with other services
builder.Services.AddHttpClient();

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

// Map SignalR Hub
app.MapHub<MessageHub>("/messagehub");

var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});
app.MapControllers();

// Schema is managed by EF Core migrations (apply with `dotnet ef database update`),
// matching AuthService/ListingService. EnsureCreated() was removed.
app.Run();