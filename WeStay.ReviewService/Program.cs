using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WeStay.ReviewService.Data;
using WeStay.ReviewService.Services;

var builder = WebApplication.CreateBuilder(args);

// Database (EF Core migrations — apply with `dotnet ef database update`).
var connectionString = builder.Configuration.GetConnectionString("ReviewConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'ReviewConnection' is not configured. " +
        "Set it via User Secrets or an environment variable (it must not be committed to appsettings.json).");
builder.Services.AddDbContext<ReviewDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddHttpClient();

// JWT Authentication (unified with the rest of WeStay).
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

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "WeStay Review Service API",
        Version = "v1",
        Description = "Reviews and star ratings microservice for WeStay"
    });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your valid token"
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
            new string[] { }
        }
    });
});

var app = builder.Build();

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

app.Run();
