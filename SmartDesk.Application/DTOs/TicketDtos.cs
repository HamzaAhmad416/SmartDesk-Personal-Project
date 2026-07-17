using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.DTOs;

/// <summary>
/// WHY DTOs (Data Transfer Objects)?
///
/// We never expose Domain entities directly to the API or Blazor UI because:
/// 1. Domain entities have private setters — JSON serializers can't populate them
/// 2. Entities may have circular references (Ticket → Comments → Ticket)
/// 3. You may want to expose LESS data than what's in the entity (security)
/// 4. API response shape should be independent of internal domain model
///
/// DTOs are simple records with only what the caller needs.
/// Records in C# are immutable by default — perfect for response objects.
/// </summary>

// ── Requests (what comes IN from the client) ──────────────────────────────────

public record CreateTicketRequest(
    string Title,
    string Description,
    TicketPriority Priority
);

public record AssignTicketRequest(
    Guid AgentId
);

public record CreateCommentRequest(
    string Body,
    bool IsInternal = false
);

// ── Responses (what goes OUT to the client) ───────────────────────────────────

/// <summary>Lightweight — used in lists and DataGrid. No comments/attachments.</summary>
public record TicketListDto(
    Guid Id,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    TicketCategory Category,
    string SubmittedBy,
    string? AssignedAgent,
    DateTime CreatedAt,
    DateTime? ResolvedAt
);

/// <summary>Full detail — used on the ticket detail page. Includes everything.</summary>
public record TicketDetailDto(
    Guid Id,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    TicketCategory Category,
    string SubmittedBy,
    string? AssignedAgent,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? AiSuggestedReply,
    IEnumerable<CommentDto> Comments,
    IEnumerable<AttachmentDto> Attachments
);

public record CommentDto(
    Guid Id,
    string Body,
    string Author,
    bool IsInternal,
    bool IsAiGenerated,
    DateTime CreatedAt
);

public record AttachmentDto(
    Guid Id,
    string FileName,
    string BlobUrl,
    long FileSizeBytes,
    DateTime CreatedAt
);

public record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    UserRole Role
);

// ── Dashboard ─────────────────────────────────────────────────────────────────

/// <summary>
/// Returned by GET /api/v1/dashboard/stats.
/// This is what drives the Radzen charts on the dashboard page.
/// Result is Redis-cached for 10 minutes.
/// </summary>
public record DashboardStatsDto(
    int TotalOpen,
    int TotalInProgress,
    int TotalResolved,
    int TotalClosed,
    double AvgResolutionHours,
    IEnumerable<CategoryCountDto> ByCategory
);

public record CategoryCountDto(
    string Category,
    int Count
);
