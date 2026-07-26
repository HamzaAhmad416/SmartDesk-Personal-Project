using Newtonsoft.Json;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Cosmos;

/// <summary>
/// WHY A SEPARATE DOCUMENT MODEL?
///
/// Our Domain entities (Ticket.cs) have private setters enforcing business rules.
/// CosmosDB deserializer needs public setters to reconstruct objects from JSON.
/// 
/// Solution: two separate classes.
/// - CosmosTicketDocument: plain serializable class (what Cosmos stores)
/// - Ticket: rich domain object with behaviour (what the app uses)
///
/// The document maps TO and FROM the entity. This boundary is the 
/// Infrastructure layer's job — it translates between storage format and domain.
///
/// WHY NESTED COMMENTS AND ATTACHMENTS IN ONE DOCUMENT?
/// In CosmosDB, reading a ticket + its comments in SQL would need a JOIN.
/// In Cosmos, they're all in one JSON document — one read, zero joins.
/// This is why CosmosDB fits the ticket domain perfectly.
/// </summary>
public class CosmosTicketDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("priority")]
    public string Priority { get; set; } = string.Empty;

    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonProperty("submittedByUserId")]
    public string SubmittedByUserId { get; set; } = string.Empty;

    [JsonProperty("assignedAgentId")]
    public string? AssignedAgentId { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonProperty("resolvedAt")]
    public DateTime? ResolvedAt { get; set; }

    [JsonProperty("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [JsonProperty("aiSuggestedReply")]
    public string? AiSuggestedReply { get; set; }

    [JsonProperty("isDeleted")]
    public bool IsDeleted { get; set; }

    // Nested arrays — stored inside the ticket document
    [JsonProperty("comments")]
    public List<CosmosCommentDocument> Comments { get; set; } = new();

    [JsonProperty("attachments")]
    public List<CosmosAttachmentDocument> Attachments { get; set; } = new();

    // ── Entity → Document (for saving TO Cosmos) ──────────────────────────────

    public static CosmosTicketDocument FromEntity(Ticket t) => new()
    {
        Id = t.Id.ToString(),
        Title = t.Title,
        Description = t.Description,
        Status = t.Status.ToString(),
        Priority = t.Priority.ToString(),
        Category = t.Category.ToString(),
        SubmittedByUserId = t.SubmittedByUserId.ToString(),
        AssignedAgentId = t.AssignedAgentId?.ToString(),
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        ResolvedAt = t.ResolvedAt,
        CreatedBy = t.CreatedBy,
        AiSuggestedReply = t.AiSuggestedReply,
        Comments = t.Comments.Select(CosmosCommentDocument.FromEntity).ToList(),
        Attachments = t.Attachments.Select(CosmosAttachmentDocument.FromEntity).ToList()
    };

    // ── Document → Entity (for reading FROM Cosmos) ───────────────────────────

    public Ticket ToEntity() => CosmosEntityFactory.CreateTicket(
        id: Guid.Parse(Id),
        title: Title,
        description: Description,
        status: Enum.Parse<TicketStatus>(Status),
        priority: Enum.Parse<TicketPriority>(Priority),
        category: Enum.Parse<TicketCategory>(Category),
        submittedByUserId: Guid.Parse(SubmittedByUserId),
        assignedAgentId: AssignedAgentId != null ? Guid.Parse(AssignedAgentId) : null,
        createdAt: CreatedAt,
        updatedAt: UpdatedAt,
        resolvedAt: ResolvedAt,
        createdBy: CreatedBy,
        aiSuggestedReply: AiSuggestedReply
    );
}

public class CosmosCommentDocument
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("body")] public string Body { get; set; } = string.Empty;
    [JsonProperty("ticketId")] public string TicketId { get; set; } = string.Empty;
    [JsonProperty("authorId")] public string AuthorId { get; set; } = string.Empty;
    [JsonProperty("isInternal")] public bool IsInternal { get; set; }
    [JsonProperty("isAiGenerated")] public bool IsAiGenerated { get; set; }
    [JsonProperty("createdAt")] public DateTime CreatedAt { get; set; }
    [JsonProperty("createdBy")] public string CreatedBy { get; set; } = string.Empty;

    public static CosmosCommentDocument FromEntity(Comment c) => new()
    {
        Id = c.Id.ToString(),
        Body = c.Body,
        TicketId = c.TicketId.ToString(),
        AuthorId = c.AuthorId.ToString(),
        IsInternal = c.IsInternal,
        IsAiGenerated = c.IsAiGenerated,
        CreatedAt = c.CreatedAt,
        CreatedBy = c.CreatedBy
    };

    public Comment ToEntity() =>
        Comment.Create(Body, Guid.Parse(TicketId), Guid.Parse(AuthorId), CreatedBy, IsInternal, IsAiGenerated);
}

public class CosmosAttachmentDocument
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("fileName")] public string FileName { get; set; } = string.Empty;
    [JsonProperty("blobName")] public string BlobName { get; set; } = string.Empty;
    [JsonProperty("blobUrl")] public string BlobUrl { get; set; } = string.Empty;
    [JsonProperty("contentType")] public string ContentType { get; set; } = string.Empty;
    [JsonProperty("fileSizeBytes")] public long FileSizeBytes { get; set; }
    [JsonProperty("createdAt")] public DateTime CreatedAt { get; set; }
    [JsonProperty("createdBy")] public string CreatedBy { get; set; } = string.Empty;

    public static CosmosAttachmentDocument FromEntity(Attachment a) => new()
    {
        Id = a.Id.ToString(),
        FileName = a.FileName,
        BlobName = a.BlobName,
        BlobUrl = a.BlobUrl,
        ContentType = a.ContentType,
        FileSizeBytes = a.FileSizeBytes,
        CreatedAt = a.CreatedAt,
        CreatedBy = a.CreatedBy
    };
}

// ── User Document ─────────────────────────────────────────────────────────────

public class CosmosUserDocument
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("email")] public string Email { get; set; } = string.Empty;
    [JsonProperty("displayName")] public string DisplayName { get; set; } = string.Empty;
    [JsonProperty("role")] public string Role { get; set; } = string.Empty;
    [JsonProperty("isActive")] public bool IsActive { get; set; }
    [JsonProperty("azureAdObjectId")] public string? AzureAdObjectId { get; set; }
    [JsonProperty("createdAt")] public DateTime CreatedAt { get; set; }
    [JsonProperty("createdBy")] public string CreatedBy { get; set; } = string.Empty;

    public static CosmosUserDocument FromEntity(AppUser u) => new()
    {
        Id = u.Id.ToString(),
        Email = u.Email,
        DisplayName = u.DisplayName,
        Role = u.Role.ToString(),
        IsActive = u.IsActive,
        AzureAdObjectId = u.AzureAdObjectId,
        CreatedAt = u.CreatedAt,
        CreatedBy = u.CreatedBy
    };

    public AppUser ToEntity() =>
        CosmosEntityFactory.CreateUser(
            Guid.Parse(Id), Email, DisplayName,
            Enum.Parse<UserRole>(Role),
            IsActive, AzureAdObjectId, CreatedAt, CreatedBy);
}
