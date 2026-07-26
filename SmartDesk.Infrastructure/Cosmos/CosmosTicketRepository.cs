using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using SmartDesk.Application.Interfaces;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Cosmos;

/// <summary>
/// WHY COSMOSDB FOR TICKETS?
///
/// Tickets are document-shaped — one ticket has nested comments and attachments.
/// In SQL you'd need 3 tables and JOINs to get one ticket with everything.
/// In CosmosDB, one ticket IS one JSON document — all data in one read.
///
/// PARTITION KEY STRATEGY:
/// We use the ticket's own Id as partition key.
/// Why? Most queries are "get ticket by id" — point reads hit exactly one partition.
/// Point reads are the cheapest operation in Cosmos (1 RU flat rate).
///
/// UNIT OF WORK INTEGRATION:
/// Update() and Delete() don't hit Cosmos immediately.
/// They queue the entity in PendingUpserts/PendingDeletes.
/// CosmosUnitOfWork.SaveChangesAsync() flushes them all at once.
/// Same mental model as EF Core — nothing saves until you say so.
/// </summary>
public class CosmosTicketRepository : ITicketRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosTicketRepository> _logger;

    internal readonly List<Ticket> PendingUpserts = new();
    internal readonly List<Guid> PendingDeletes = new();

    public CosmosTicketRepository(
        CosmosClient cosmosClient,
        string databaseName,
        string containerName,
        ILogger<CosmosTicketRepository> logger)
    {
        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    // Point read — fastest Cosmos operation, costs exactly 1 RU
    public async Task<Ticket?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<CosmosTicketDocument>(
                id.ToString(),
                new PartitionKey(id.ToString()),
                cancellationToken: ct);

            return response.Resource.ToEntity();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // Full document read — includes comments and attachments (no extra queries needed)
    public async Task<Ticket?> GetWithDetailsAsync(Guid id, CancellationToken ct = default)
        => await GetByIdAsync(id, ct); // In Cosmos, full document IS the detail

    public async Task<IEnumerable<Ticket>> GetAllAsync(CancellationToken ct = default)
    {
        var query = _container
            .GetItemLinqQueryable<CosmosTicketDocument>()
            .Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .ToFeedIterator();

        return await DrainIteratorAsync(query, ct);
    }

    public async Task<IEnumerable<Ticket>> GetByStatusAsync(TicketStatus status, CancellationToken ct = default)
    {
        var statusStr = status.ToString();
        var query = _container
            .GetItemLinqQueryable<CosmosTicketDocument>()
            .Where(t => t.Status == statusStr && !t.IsDeleted)
            .ToFeedIterator();

        return await DrainIteratorAsync(query, ct);
    }

    public async Task<IEnumerable<Ticket>> GetByAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        var agentIdStr = agentId.ToString();
        var query = _container
            .GetItemLinqQueryable<CosmosTicketDocument>()
            .Where(t => t.AssignedAgentId == agentIdStr && !t.IsDeleted)
            .ToFeedIterator();

        return await DrainIteratorAsync(query, ct);
    }

    public async Task<IEnumerable<Ticket>> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var userIdStr = userId.ToString();
        var query = _container
            .GetItemLinqQueryable<CosmosTicketDocument>()
            .Where(t => t.SubmittedByUserId == userIdStr && !t.IsDeleted)
            .ToFeedIterator();

        return await DrainIteratorAsync(query, ct);
    }

    public async Task<int> GetOpenCountAsync(CancellationToken ct = default)
    {
        var openStr = TicketStatus.Open.ToString();
        return await _container
            .GetItemLinqQueryable<CosmosTicketDocument>()
            .Where(t => t.Status == openStr && !t.IsDeleted)
            .CountAsync(ct);
    }

    // CreateItemAsync immediately writes to Cosmos — new items don't need UoW
    public async Task AddAsync(Ticket entity, CancellationToken ct = default)
    {
        var doc = CosmosTicketDocument.FromEntity(entity);
        await _container.CreateItemAsync(
            doc,
            new PartitionKey(doc.Id),
            cancellationToken: ct);

        _logger.LogInformation("Ticket {TicketId} written to Cosmos", entity.Id);
    }

    // Queued — flushed by UnitOfWork.SaveChangesAsync()
    public void Update(Ticket entity) => PendingUpserts.Add(entity);

    // Soft delete — we never hard delete tickets (audit trail requirement)
    public void Delete(Ticket entity) => PendingDeletes.Add(entity.Id);

    internal async Task FlushAsync(CancellationToken ct)
    {
        foreach (var ticket in PendingUpserts)
        {
            var doc = CosmosTicketDocument.FromEntity(ticket);
            await _container.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
            _logger.LogDebug("Ticket {TicketId} upserted to Cosmos", ticket.Id);
        }

        foreach (var id in PendingDeletes)
        {
            // Read → mark deleted → upsert (soft delete)
            var existing = await GetByIdAsync(id, ct);
            if (existing is not null)
            {
                var doc = CosmosTicketDocument.FromEntity(existing);
                doc.IsDeleted = true;
                await _container.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
            }
        }

        PendingUpserts.Clear();
        PendingDeletes.Clear();
    }

    // Drains a paginated Cosmos feed iterator into a flat list
    // WHY: Cosmos returns results in pages. We iterate all pages here.
    private static async Task<IEnumerable<Ticket>> DrainIteratorAsync(
        FeedIterator<CosmosTicketDocument> iterator,
        CancellationToken ct)
    {
        var results = new List<Ticket>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }
}
