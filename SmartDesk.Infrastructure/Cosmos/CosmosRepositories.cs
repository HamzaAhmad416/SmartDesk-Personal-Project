using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using SmartDesk.Application.Interfaces;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Cosmos;

/// <summary>
/// WHY USERS IN COSMOS TOO (instead of SQL)?
/// Consistency — everything in one database.
/// Users are also document-shaped: profile + settings + preferences.
/// 
/// PARTITION KEY: user's own Id.
/// Most common query is "get user by Azure AD object id on login" —
/// we handle that with a cross-partition query (small dataset, acceptable cost).
/// </summary>
public class CosmosUserRepository : IUserRepository
{
    private readonly Container _container;
    internal readonly List<AppUser> PendingUpserts = new();

    public CosmosUserRepository(CosmosClient cosmosClient, string databaseName, string containerName)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
    }

    public async Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<CosmosUserDocument>(
                id.ToString(), new PartitionKey(id.ToString()), cancellationToken: ct);
            return response.Resource.ToEntity();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var emailLower = email.ToLowerInvariant();
        var query = _container.GetItemLinqQueryable<CosmosUserDocument>()
            .Where(u => u.Email == emailLower)
            .ToFeedIterator();

        return (await DrainAsync(query, ct)).FirstOrDefault();
    }

    public async Task<AppUser?> GetByAzureIdAsync(string azureObjectId, CancellationToken ct = default)
    {
        var query = _container.GetItemLinqQueryable<CosmosUserDocument>()
            .Where(u => u.AzureAdObjectId == azureObjectId)
            .ToFeedIterator();

        return (await DrainAsync(query, ct)).FirstOrDefault();
    }

    public async Task<IEnumerable<AppUser>> GetAgentsAsync(CancellationToken ct = default)
    {
        var agentRole = UserRole.Agent.ToString();
        var query = _container.GetItemLinqQueryable<CosmosUserDocument>()
            .Where(u => u.Role == agentRole && u.IsActive)
            .ToFeedIterator();

        return await DrainAsync(query, ct);
    }

    public async Task<IEnumerable<AppUser>> GetAllAsync(CancellationToken ct = default)
    {
        var query = _container.GetItemLinqQueryable<CosmosUserDocument>()
            .Where(u => u.IsActive)
            .ToFeedIterator();

        return await DrainAsync(query, ct);
    }

    public async Task AddAsync(AppUser entity, CancellationToken ct = default)
    {
        var doc = CosmosUserDocument.FromEntity(entity);
        await _container.CreateItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
    }

    public void Update(AppUser entity) => PendingUpserts.Add(entity);
    public void Delete(AppUser entity) { /* Users are deactivated not deleted */ }

    internal async Task FlushAsync(CancellationToken ct)
    {
        foreach (var user in PendingUpserts)
        {
            var doc = CosmosUserDocument.FromEntity(user);
            await _container.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
        }
        PendingUpserts.Clear();
    }

    private static async Task<IEnumerable<AppUser>> DrainAsync(
        FeedIterator<CosmosUserDocument> iterator, CancellationToken ct)
    {
        var results = new List<AppUser>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }
}

/// <summary>
/// WHY A SEPARATE COMMENTS CONTAINER?
/// Comments are stored NESTED inside tickets (for fast ticket reads).
/// But they're ALSO in this separate container for agent-level queries:
/// "show me all comments posted by agent X this week"
/// This is called DENORMALIZATION — storing the same data twice for different query patterns.
/// It's a core Cosmos design pattern and demonstrates real cloud database knowledge.
/// </summary>
public class CosmosCommentRepository : ICommentRepository
{
    private readonly Container _container;

    public CosmosCommentRepository(CosmosClient cosmosClient, string databaseName, string containerName)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
    }

    public async Task<Comment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<CosmosCommentDocument>(
                id.ToString(), new PartitionKey(id.ToString()), cancellationToken: ct);
            return response.Resource.ToEntity();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IEnumerable<Comment>> GetByTicketAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticketIdStr = ticketId.ToString();
        var query = _container.GetItemLinqQueryable<CosmosCommentDocument>()
            .Where(c => c.TicketId == ticketIdStr)
            .OrderBy(c => c.CreatedAt)
            .ToFeedIterator();

        var results = new List<Comment>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public async Task<IEnumerable<Comment>> GetAllAsync(CancellationToken ct = default)
    {
        var query = _container.GetItemLinqQueryable<CosmosCommentDocument>().ToFeedIterator();
        var results = new List<Comment>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    // Comments are written to BOTH here AND inside the ticket document (via ticket flush)
    public async Task AddAsync(Comment entity, CancellationToken ct = default)
    {
        var doc = CosmosCommentDocument.FromEntity(entity);
        await _container.CreateItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
    }

    public void Update(Comment entity) { /* Comments are immutable once posted */ }
    public void Delete(Comment entity) { /* No hard deletes on comments */ }
}
