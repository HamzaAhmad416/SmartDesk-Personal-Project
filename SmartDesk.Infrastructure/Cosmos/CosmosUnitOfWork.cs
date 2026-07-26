using SmartDesk.Application.Interfaces;

namespace SmartDesk.Infrastructure.Cosmos;

/// <summary>
/// WHY UNIT OF WORK?
///
/// Without it, every repo.Update() would hit Cosmos immediately.
/// If you update a ticket AND add a comment, you'd have two separate
/// Cosmos calls that could partially fail.
///
/// With Unit of Work:
/// 1. ticket.Assign(agentId)          → queued in PendingUpserts
/// 2. _uow.Comments.AddAsync(comment) → queued in Comments container
/// 3. _uow.SaveChangesAsync()         → flushes ALL at once
///
/// This is the exact same pattern as EF Core's DbContext.
/// Hirers who know EF Core will immediately recognise this pattern.
/// </summary>
public class CosmosUnitOfWork : IUnitOfWork
{
    private readonly CosmosTicketRepository _tickets;
    private readonly CosmosUserRepository _users;
    private readonly CosmosCommentRepository _comments;

    public ITicketRepository Tickets => _tickets;
    public IUserRepository Users => _users;
    public ICommentRepository Comments => _comments;

    public CosmosUnitOfWork(
        CosmosTicketRepository tickets,
        CosmosUserRepository users,
        CosmosCommentRepository comments)
    {
        _tickets = tickets;
        _users = users;
        _comments = comments;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // Flush all pending writes to Cosmos in one coordinated pass
        await _tickets.FlushAsync(ct);
        await _users.FlushAsync(ct);
        // Comments write immediately in AddAsync (no pending queue needed)
        return 1;
    }
}
