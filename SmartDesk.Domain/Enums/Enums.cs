namespace SmartDesk.Domain.Enums;

/// <summary>
/// WHY ENUMS IN DOMAIN:
/// These represent the allowed states and categories of our business objects.
/// They live in Domain because they are business rules, not technical details.
/// e.g. "A ticket can only be Open, InProgress, Resolved or Closed" is a
/// business rule — it belongs here, not in the database or UI.
/// </summary>

public enum TicketStatus
{
    Open = 1,
    InProgress = 2,
    PendingUser = 3,
    Resolved = 4,
    Closed = 5
}

public enum TicketPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// AI will auto-assign this category via Azure Function + OpenAI.
/// Starts as Uncategorised when ticket is created.
/// </summary>
public enum TicketCategory
{
    Uncategorised = 0,
    Hardware = 1,
    Software = 2,
    Network = 3,
    Security = 4,
    Access = 5,
    Other = 6
}

public enum UserRole
{
    User = 1,    // Can submit tickets
    Agent = 2,   // Can assign + resolve tickets
    Admin = 3    // Full access including user management
}
