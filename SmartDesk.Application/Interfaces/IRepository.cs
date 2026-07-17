using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Interfaces;

/// <summary>
/// WHY THE REPOSITORY PATTERN:
/// Application layer needs to read/write data but should NOT know it's CosmosDB.
/// These interfaces are the contract. Infrastructure implements them.
/// This means you could swap CosmosDB for SQL Server tomorrow by just writing
/// a new implementation — Application and Domain never change.
/// This is the "D" in SOLID: Dependency Inversion Principle.
/// </summary>

// Generic base — all repos get these 5 operations for free
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Delete(T entity);
}

/// <summary>
/// WHY UNIT OF WORK:
/// Coordinates multiple repositories under ONE save operation.
/// Instead of each repo saving independently (risking partial saves),
/// you call _uow.SaveChangesAsync() once at the end.
/// Same mental model as EF Core's DbContext.SaveChanges().
/// </summary>
public interface IUnitOfWork
{
    ITicketRepository Tickets { get; }
    IUserRepository Users { get; }
    ICommentRepository Comments { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

// Ticket-specific queries beyond the generic CRUD
public interface ITicketRepository : IRepository<Ticket>
{
    Task<IEnumerable<Ticket>> GetByStatusAsync(TicketStatus status, CancellationToken ct = default);
    Task<IEnumerable<Ticket>> GetByAgentAsync(Guid agentId, CancellationToken ct = default);
    Task<IEnumerable<Ticket>> GetByUserAsync(Guid userId, CancellationToken ct = default);

    // Returns ticket WITH comments and attachments included (full document from Cosmos)
    Task<Ticket?> GetWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<int> GetOpenCountAsync(CancellationToken ct = default);
}

public interface IUserRepository : IRepository<AppUser>
{
    Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<AppUser?> GetByAzureIdAsync(string azureObjectId, CancellationToken ct = default);
    Task<IEnumerable<AppUser>> GetAgentsAsync(CancellationToken ct = default);
}

public interface ICommentRepository : IRepository<Comment>
{
    Task<IEnumerable<Comment>> GetByTicketAsync(Guid ticketId, CancellationToken ct = default);
}
