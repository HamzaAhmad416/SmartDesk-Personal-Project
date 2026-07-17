namespace SmartDesk.Domain.Common;

/// <summary>
/// WHY: Every entity in the system (Ticket, User, Comment) needs an Id,
/// creation date, and who created it. Instead of repeating these fields
/// on every class, we put them here once. All entities inherit from this.
/// 
/// This is the DRY principle (Don't Repeat Yourself) applied at the domain level.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public string CreatedBy { get; protected set; } = string.Empty;

    /// <summary>
    /// Called by entities when they change state — stamps the UpdatedAt time.
    /// </summary>
    protected void SetUpdated() => UpdatedAt = DateTime.UtcNow;
}
