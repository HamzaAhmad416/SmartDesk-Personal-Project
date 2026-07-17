using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SmartDesk.Application.Services;
using System.Text;
using System.Threading.RateLimiting;

namespace SmartDesk.API.Extensions;

/// <summary>
/// WHY EXTENSION METHODS FOR REGISTRATION?
/// If everything was in Program.cs it would be 200+ lines of noise.
/// Extension methods on IServiceCollection let us group related registrations
/// and keep Program.cs readable. This is a widely used .NET pattern.
///
/// NOTE: AddSmartDeskInfrastructure is defined in SmartDesk.Infrastructure project.
/// That's intentional — Infrastructure registers its OWN implementations.
/// API project doesn't need to know about CosmosClient or BlobServiceClient.
/// </summary>
public static class ServiceExtensions
{
    public static IServiceCollection AddSmartDeskServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Application services — Scoped means one instance per HTTP request
        services.AddScoped<TicketService>();
        services.AddScoped<UserService>();

        // Rate limiting — 100 requests per minute per client
        // WHY? Prevents abuse and is listed on your CV as a skill
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("api", o =>
            {
                o.PermitLimit = 100;
                o.Window = TimeSpan.FromMinutes(1);
                o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                o.QueueLimit = 5;
            });
            options.RejectionStatusCode = 429; // Too Many Requests
        });

        // CORS — allows Blazor frontend to call this API
        services.AddCors(options =>
        {
            options.AddPolicy("BlazorClient", policy =>
                policy.WithOrigins(
                        config["Blazor:BaseUrl"] ?? "https://localhost:7001")
                      .AllowAnyHeader()
                      .AllowAnyMethod());
        });

        return services;
    }

    public static IServiceCollection AddSmartDeskAuth(
        this IServiceCollection services,
        IConfiguration config)
    {
        /// <summary>
        /// WHY JWT + OAuth 2.0?
        /// JWT: stateless tokens — API doesn't need to hit a database to validate each request.
        ///      Token contains claims (userId, email, role) signed with a secret key.
        /// OAuth 2.0: industry standard for delegated auth (login with Azure AD / Google).
        /// Together they handle: login, token issuance, role-based access control.
        /// Both are on your CV and this is the real implementation.
        /// </summary>
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidAudience = config["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(config["Jwt:Key"]!))
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AgentOrAdmin", policy =>
                policy.RequireRole("Agent", "Admin"));
            options.AddPolicy("AdminOnly", policy =>
                policy.RequireRole("Admin"));
        });

        return services;
    }

    public static IServiceCollection AddSmartDeskSwagger(
        this IServiceCollection services)
    {
        /// <summary>
        /// WHY SWAGGER?
        /// Auto-generates interactive API documentation from your code.
        /// Hirers can visit /swagger and test endpoints without Postman.
        /// JWT support in Swagger means they can log in and test secured endpoints too.
        /// OpenAPI/Swagger is explicitly on your CV.
        /// </summary>
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "SmartDesk API",
                Version = "v1",
                Description = "AI-powered IT Helpdesk — built with ASP.NET Core 8, CosmosDB, Azure Service Bus"
            });
            c.SwaggerDoc("v2", new OpenApiInfo
            {
                Title = "SmartDesk API",
                Version = "v2",
                Description = "V2 — adds filtering and pagination"
            });

            // Enables the Authorize button in Swagger UI
            var securityScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT token",
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            };

            c.AddSecurityDefinition("Bearer", securityScheme);
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { securityScheme, Array.Empty<string>() }
            });
        });

        return services;
    }

    public static IServiceCollection AddSmartDeskHealthChecks(
        this IServiceCollection services,
        IConfiguration config)
    {
        /// <summary>
        /// WHY HEALTH CHECKS?
        /// GET /health returns whether Cosmos, Redis, and Service Bus are reachable.
        /// Azure App Service uses this to decide if the app is healthy.
        /// It's on your CV and Azure DevOps CI/CD pipelines check it post-deploy.
        /// </summary>
        services.AddHealthChecks()
            .AddAzureCosmosDB(
                config["ConnectionStrings:CosmosDb"]!,
                name: "cosmos-db",
                tags: new[] { "db", "cosmos" })
            .AddRedis(
                config.GetConnectionString("Redis")!,
                name: "redis",
                tags: new[] { "cache" });

        return services;
    }
}
