using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using SmartDesk.Application.Interfaces;
using SmartDesk.Infrastructure.Cache;
using SmartDesk.Infrastructure.Cosmos;
using SmartDesk.Infrastructure.Messaging;
using SmartDesk.Infrastructure.Storage;

namespace SmartDesk.Infrastructure.Extensions;

/// <summary>
/// WHY DOES INFRASTRUCTURE REGISTER ITS OWN SERVICES?
///
/// The API project calls AddSmartDeskInfrastructure() but never imports
/// CosmosClient, BlobServiceClient, or StackExchange.Redis directly.
/// 
/// This keeps the API clean — it depends only on Application interfaces.
/// Infrastructure wires up the concrete implementations against those interfaces.
///
/// This completes the Dependency Inversion Principle full circle:
/// - Application defines: ITicketRepository, ICacheService, etc.
/// - Infrastructure implements: CosmosTicketRepository, RedisCacheService, etc.
/// - DI container maps them: services.AddScoped<ITicketRepository, CosmosTicketRepository>
/// - API uses: ITicketRepository injected into TicketService (never sees Cosmos)
///
/// SINGLETON vs SCOPED vs TRANSIENT:
/// - Singleton: created once for app lifetime. Use for thread-safe, expensive-to-create clients.
///   CosmosClient, BlobServiceClient, ServiceBusClient, IConnectionMultiplexer = Singleton
/// - Scoped: created once per HTTP request. Use for repos and services that hold request state.
///   Repositories, UnitOfWork, application services = Scoped
/// - Transient: new instance every injection. Use for lightweight, stateless utilities.
/// </summary>
public static class InfrastructureExtensions
{
    public static IServiceCollection AddSmartDeskInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        services
            .AddCosmosDb(config)
            .AddRedis(config)
            .AddBlobStorage(config)
            .AddServiceBus(config);

        return services;
    }

    private static IServiceCollection AddCosmosDb(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config["ConnectionStrings:CosmosDb"]!;
        var dbName = config["CosmosDb:DatabaseName"]!;
        var ticketsContainer = config["CosmosDb:TicketsContainer"]!;
        var usersContainer = config["CosmosDb:UsersContainer"]!;
        var commentsContainer = config["CosmosDb:CommentsContainer"]!;

        // CosmosClient is expensive to create and thread-safe — Singleton
        services.AddSingleton(_ => new CosmosClient(connectionString, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            },
            // Built-in Cosmos retry on 429 (rate limited) responses
            MaxRetryAttemptsOnRateLimitedRequests = 3,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30)
        }));

        // Repositories are Scoped — one per request, hold pending upsert lists
        services.AddScoped(sp => new CosmosTicketRepository(
            sp.GetRequiredService<CosmosClient>(), dbName, ticketsContainer,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CosmosTicketRepository>>()));

        services.AddScoped(sp => new CosmosUserRepository(
            sp.GetRequiredService<CosmosClient>(), dbName, usersContainer));

        services.AddScoped(sp => new CosmosCommentRepository(
            sp.GetRequiredService<CosmosClient>(), dbName, commentsContainer));

        // UnitOfWork wraps all three repos — Scoped so it shares same instances
        services.AddScoped<IUnitOfWork, CosmosUnitOfWork>();

        return services;
    }

    private static IServiceCollection AddRedis(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Redis")!;

        // IConnectionMultiplexer manages the Redis connection pool — must be Singleton
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(connectionString));

        services.AddScoped<ICacheService, RedisCacheService>();

        return services;
    }

    private static IServiceCollection AddBlobStorage(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config["AzureBlobStorage:ConnectionString"]!;
        var containerName = config["AzureBlobStorage:ContainerName"]!;

        // BlobServiceClient is thread-safe — Singleton
        services.AddSingleton(_ => new BlobServiceClient(connectionString));

        services.AddScoped<IBlobStorageService>(sp => new AzureBlobStorageService(
            sp.GetRequiredService<BlobServiceClient>(),
            containerName,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureBlobStorageService>>()));

        return services;
    }

    private static IServiceCollection AddServiceBus(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config["AzureServiceBus:ConnectionString"]!;
        var topicName = config["AzureServiceBus:TopicName"]!;

        // ServiceBusClient is thread-safe — Singleton
        services.AddSingleton(_ => new ServiceBusClient(connectionString));

        services.AddScoped<IServiceBusPublisher>(sp => new ServiceBusPublisher(
            sp.GetRequiredService<ServiceBusClient>(),
            topicName,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceBusPublisher>>()));

        return services;
    }
}
