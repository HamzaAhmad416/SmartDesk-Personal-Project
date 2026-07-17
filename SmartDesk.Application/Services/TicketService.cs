using Microsoft.Extensions.Logging;
using SmartDesk.Application.DTOs;
using SmartDesk.Application.Interfaces;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Services;

/// <summary>
/// WHY THIS IS AN APPLICATION SERVICE (not a domain service):
///
/// TicketService coordinates multiple things:
/// - Calls repositories (via IUnitOfWork)
/// - Invalidates cache (via ICacheService)
/// - Publishes events (via IServiceBusPublisher)
///
/// Domain entities (Ticket.cs) hold the BUSINESS RULES.
/// This service holds the APPLICATION WORKFLOW (what happens step by step).
///
/// Example: Creating a ticket involves:
/// 1. Build the entity (domain rule: title can't be empty)
/// 2. Save to Cosmos (infrastructure concern)
/// 3. Bust the cache (application concern)
/// 4. Publish event to Service Bus (application concern)
/// Only step 1 belongs in Domain. Steps 2-4 belong here.
/// </summary>
public class TicketService
{
    private readonly IUnitOfWork _uow;
    private readonly IServiceBusPublisher _bus;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketService> _logger;

    // Cache key constants — centralised so we never typo them
    private const string StatsCacheKey = "dashboard:stats";
    private const string TicketCachePrefix = "ticket:";

    public TicketService(
        IUnitOfWork uow,
        IServiceBusPublisher bus,
        ICacheService cache,
        ILogger<TicketService> logger)
    {
        _uow = uow;
        _bus = bus;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TicketDetailDto> CreateAsync(
        CreateTicketRequest request,
        Guid userId,
        string userEmail,
        CancellationToken ct = default)
    {
        // 1. Domain: create entity with business rules enforced
        var ticket = Ticket.Create(request.Title, request.Description, request.Priority, userId, userEmail);

        // 2. Infrastructure: persist to CosmosDB via repository
        await _uow.Tickets.AddAsync(ticket, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Ticket {TicketId} created by {Email}", ticket.Id, userEmail);

        // 3. Bust dashboard stats cache — counts have changed
        await _cache.RemoveAsync(StatsCacheKey, ct);

        // 4. Publish event — Azure Function will pick this up and:
        //    a) Call OpenAI to generate a reply suggestion
        //    b) Auto-categorise the ticket
        //    Both happen ASYNCHRONOUSLY — user doesn't wait for AI
        await _bus.PublishTicketCreatedAsync(ticket.Id, ct);

        return await GetDetailAsync(ticket.Id, ct)
               ?? throw new Exception("Failed to retrieve ticket after creation.");
    }

    public async Task<TicketDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        // Check Redis first — saves a Cosmos round trip on cache hit
        var cacheKey = $"{TicketCachePrefix}{id}";
        var cached = await _cache.GetAsync<TicketDetailDto>(cacheKey, ct);
        if (cached is not null) return cached;

        // Cache miss — read from Cosmos (full document with comments + attachments)
        var ticket = await _uow.Tickets.GetWithDetailsAsync(id, ct);
        if (ticket is null) return null;

        var dto = MapToDetail(ticket);

        // Store in Redis for 5 minutes
        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(5), ct);
        return dto;
    }

    public async Task<IEnumerable<TicketListDto>> GetAllAsync(CancellationToken ct = default)
    {
        var tickets = await _uow.Tickets.GetAllAsync(ct);
        return tickets.Select(MapToList);
    }

    public async Task AssignAsync(Guid ticketId, Guid agentId, CancellationToken ct = default)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(ticketId, ct)
                     ?? throw new KeyNotFoundException($"Ticket {ticketId} not found.");

        ticket.Assign(agentId);   // Domain rule: sets Status = InProgress
        _uow.Tickets.Update(ticket);
        await _uow.SaveChangesAsync(ct);

        // Invalidate this ticket's cached detail
        await _cache.RemoveAsync($"{TicketCachePrefix}{ticketId}", ct);

        // Notify agent via Service Bus → SignalR in Blazor
        await _bus.PublishTicketAssignedAsync(ticketId, agentId, ct);
    }

    public async Task ResolveAsync(Guid ticketId, CancellationToken ct = default)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(ticketId, ct)
                     ?? throw new KeyNotFoundException($"Ticket {ticketId} not found.");

        ticket.Resolve();   // Domain rule: sets Status = Resolved + stamps ResolvedAt
        _uow.Tickets.Update(ticket);
        await _uow.SaveChangesAsync(ct);

        await _cache.RemoveAsync($"{TicketCachePrefix}{ticketId}", ct);
        await _cache.RemoveAsync(StatsCacheKey, ct);   // Stats changed
        await _bus.PublishTicketResolvedAsync(ticketId, ct);
    }

    public async Task AddCommentAsync(
        Guid ticketId,
        CreateCommentRequest request,
        Guid authorId,
        string authorEmail,
        CancellationToken ct = default)
    {
        var comment = Comment.Create(request.Body, ticketId, authorId, authorEmail, request.IsInternal);
        await _uow.Comments.AddAsync(comment, ct);
        await _uow.SaveChangesAsync(ct);

        // Bust ticket cache so comment appears immediately on next load
        await _cache.RemoveAsync($"{TicketCachePrefix}{ticketId}", ct);
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        // Stats are expensive to compute — cached 10 minutes
        var cached = await _cache.GetAsync<DashboardStatsDto>(StatsCacheKey, ct);
        if (cached is not null) return cached;

        var all = (await _uow.Tickets.GetAllAsync(ct)).ToList();
        var resolved = all.Where(t => t.ResolvedAt.HasValue).ToList();
        var avgHours = resolved.Any()
            ? resolved.Average(t => t.ResolutionTime!.Value.TotalHours)
            : 0;

        var stats = new DashboardStatsDto(
            TotalOpen: all.Count(t => t.Status == TicketStatus.Open),
            TotalInProgress: all.Count(t => t.Status == TicketStatus.InProgress),
            TotalResolved: all.Count(t => t.Status == TicketStatus.Resolved),
            TotalClosed: all.Count(t => t.Status == TicketStatus.Closed),
            AvgResolutionHours: Math.Round(avgHours, 1),
            ByCategory: all
                .GroupBy(t => t.Category.ToString())
                .Select(g => new CategoryCountDto(g.Key, g.Count()))
        );

        await _cache.SetAsync(StatsCacheKey, stats, TimeSpan.FromMinutes(10), ct);
        return stats;
    }

    // ── Private Mappers ───────────────────────────────────────────────────────
    // WHY private mappers here instead of AutoMapper?
    // Explicit mapping is more readable and easier to debug.
    // AutoMapper adds a dependency and hides what's happening.
    // For a portfolio project, explicit is better — hirers can read it.

    private static TicketListDto MapToList(Ticket t) => new(
        t.Id,
        t.Title,
        t.Status,
        t.Priority,
        t.Category,
        t.SubmittedBy?.DisplayName ?? t.CreatedBy,
        t.AssignedAgent?.DisplayName,
        t.CreatedAt,
        t.ResolvedAt
    );

    private static TicketDetailDto MapToDetail(Ticket t) => new(
        t.Id,
        t.Title,
        t.Description,
        t.Status,
        t.Priority,
        t.Category,
        t.SubmittedBy?.DisplayName ?? t.CreatedBy,
        t.AssignedAgent?.DisplayName,
        t.CreatedAt,
        t.ResolvedAt,
        t.AiSuggestedReply,
        t.Comments.Select(c => new CommentDto(
            c.Id, c.Body,
            c.Author?.DisplayName ?? c.CreatedBy,
            c.IsInternal, c.IsAiGenerated, c.CreatedAt)),
        t.Attachments.Select(a => new AttachmentDto(
            a.Id, a.FileName, a.BlobUrl, a.FileSizeBytes, a.CreatedAt))
    );
}
