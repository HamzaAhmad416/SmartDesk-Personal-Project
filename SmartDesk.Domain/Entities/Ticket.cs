using SmartDesk.Domain.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

/// <summary>
/// WHY THIS CLASS IS DESIGNED THIS WAY (Domain-Driven Design):
///
/// 1. Private setters — you cannot do ticket.Status = X from outside.
///    You MUST call ticket.Resolve() or ticket.Assign(). This means
///    business rules are enforced IN the entity, not scattered across services.
///
/// 2. Static factory method (Ticket.Create) — instead of "new Ticket()",
///    we use a named factory that makes the intent clear and validates inputs.
///
/// 3. Private constructor — forces use of Ticket.Create(), preventing
///    invalid objects from being created accidentally.
///
/// This is called an "Aggregate Root" in DDD — Ticket owns Comments and Attachments.
/// Nothing should modify Comments without going through Ticket.
/// </summary>
public class Ticket : BaseEntity
{
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public TicketStatus Status { get; private set; } = TicketStatus.Open;
    public TicketPriority Priority { get; private set; } = TicketPriority.Medium;
    public TicketCategory Category { get; private set; } = TicketCategory.Uncategorised;

    public Guid SubmittedByUserId { get; private set; }
    public AppUser? SubmittedBy { get; private set; }

    public Guid? AssignedAgentId { get; private set; }
    public AppUser? AssignedAgent { get; private set; }

    public DateTime? ResolvedAt { get; private set; }
    
    // Set by Azure Function after AI processes the ticket
    public string? AiSuggestedReply { get; private set; }

    // Navigation — owned by this aggregate
    public ICollection<Comment> Comments { get; private set; } = new List<Comment>();
    public ICollection<Attachment> Attachments { get; private set; } = new List<Attachment>();

    // Required by CosmosDB deserializer — never call directly
    private Ticket() { }

    // ── Factory Method ──────────────────────────────────────────────────────

    public static Ticket Create(
        string title,
        string description,
        TicketPriority priority,
        Guid submittedByUserId,
        string createdBy)
    {
        // Guard clauses — validate before creating
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Ticket title cannot be empty.", nameof(title));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Ticket description cannot be empty.", nameof(description));

        return new Ticket
        {
            Title = title.Trim(),
            Description = description.Trim(),
            Priority = priority,
            SubmittedByUserId = submittedByUserId,
            CreatedBy = createdBy,
            Status = TicketStatus.Open,
            Category = TicketCategory.Uncategorised
        };
    }

    // ── Business Methods ────────────────────────────────────────────────────
    // WHY methods instead of just setting properties?
    // Because business rules live here. Assign() doesn't just set AgentId —
    // it also changes status to InProgress. That logic is in ONE place.

    public void Assign(Guid agentId)
    {
        AssignedAgentId = agentId;
        Status = TicketStatus.InProgress;
        SetUpdated();
    }

    public void Resolve()
    {
        Status = TicketStatus.Resolved;
        ResolvedAt = DateTime.UtcNow;
        SetUpdated();
    }

    public void Close()
    {
        Status = TicketStatus.Closed;
        SetUpdated();
    }

    public void UpdateCategory(TicketCategory category)
    {
        Category = category;
        SetUpdated();
    }

    /// <summary>
    /// Called by the Infrastructure layer after Azure Function returns AI suggestion.
    /// </summary>
    public void SetAiSuggestedReply(string reply)
    {
        AiSuggestedReply = reply;
        SetUpdated();
    }

    /// <summary>
    /// How long did it take to resolve this ticket? Null if not resolved yet.
    /// Useful for dashboard "avg resolution time" metric.
    /// </summary>
    public TimeSpan? ResolutionTime =>
        ResolvedAt.HasValue ? ResolvedAt.Value - CreatedAt : null;
}
