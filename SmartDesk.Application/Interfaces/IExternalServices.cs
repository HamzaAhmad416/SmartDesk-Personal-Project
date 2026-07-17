namespace SmartDesk.Application.Interfaces;

/// <summary>
/// WHY THESE INTERFACES LIVE IN APPLICATION (not Infrastructure):
///
/// Application layer needs to USE blob storage, service bus, cache and AI
/// but must not depend on any Azure SDK directly. These interfaces describe
/// WHAT we need — Infrastructure provides the HOW using Azure SDKs.
///
/// This keeps Application layer free of Azure dependencies.
/// You could run it locally with fake/mock implementations of these.
/// </summary>

// ── Azure Blob Storage ────────────────────────────────────────────────────────
public interface IBlobStorageService
{
    /// <summary>Uploads a file, returns the blob name (storage path)</summary>
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, CancellationToken ct = default);

    Task DeleteAsync(string blobName, CancellationToken ct = default);

    /// <summary>
    /// Generates a time-limited SAS URL for direct download.
    /// WHY SAS? Files are served directly from Azure CDN — not through your API.
    /// Saves bandwidth and cost. URL expires after the given timespan.
    /// </summary>
    Task<string> GetSasUrlAsync(string blobName, TimeSpan expiry, CancellationToken ct = default);
}

// ── Azure Service Bus ─────────────────────────────────────────────────────────
public interface IServiceBusPublisher
{
    /// <summary>
    /// WHY publish events to Service Bus instead of calling AI directly?
    /// 1. API responds instantly — ticket is created, user gets a 201 response
    /// 2. Azure Function picks up the message async and calls OpenAI
    /// 3. If OpenAI is slow/down, tickets still work — messages just queue up
    /// This is event-driven architecture — resilient and decoupled.
    /// </summary>
    Task PublishTicketCreatedAsync(Guid ticketId, CancellationToken ct = default);
    Task PublishTicketAssignedAsync(Guid ticketId, Guid agentId, CancellationToken ct = default);
    Task PublishTicketResolvedAsync(Guid ticketId, CancellationToken ct = default);
}

// ── Redis Cache ───────────────────────────────────────────────────────────────
public interface ICacheService
{
    /// <summary>
    /// WHY Redis Cache?
    /// Dashboard stats and ticket lists are read far more than written.
    /// Instead of hitting CosmosDB (costs RUs) on every page load,
    /// we cache results in Redis (in-memory, microsecond reads).
    /// Cache is invalidated when tickets change.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}

// ── AI Service ────────────────────────────────────────────────────────────────
public interface IAiService
{
    /// <summary>Suggests a reply for the agent based on ticket content</summary>
    Task<string> SuggestReplyAsync(string title, string description, CancellationToken ct = default);

    /// <summary>Auto-categorises ticket as Hardware/Software/Network etc.</summary>
    Task<string> CategoriseTicketAsync(string title, string description, CancellationToken ct = default);
}
