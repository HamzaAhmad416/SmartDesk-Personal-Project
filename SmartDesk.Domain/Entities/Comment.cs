using SmartDesk.Domain.Common;

namespace SmartDesk.Domain.Entities;

/// <summary>
/// WHY Comment is a separate entity but nested inside Ticket in CosmosDB:
///
/// In the Domain layer, Comment is its own class (separation of concerns).
/// In CosmosDB (Infrastructure layer), comments are stored as a nested array
/// INSIDE the ticket document — because you always read them together.
/// This avoids extra round trips to the database.
///
/// IsInternal = true means only Agents/Admins can see it.
/// IsAiGenerated = true means it was created by our Azure Function AI service.
/// </summary>
public class Comment : BaseEntity
{
    public string Body { get; private set; } = string.Empty;
    public bool IsInternal { get; private set; }
    public bool IsAiGenerated { get; private set; }

    public Guid TicketId { get; private set; }
    public Ticket? Ticket { get; private set; }

    public Guid AuthorId { get; private set; }
    public AppUser? Author { get; private set; }

    private Comment() { }

    public static Comment Create(
        string body,
        Guid ticketId,
        Guid authorId,
        string createdBy,
        bool isInternal = false,
        bool isAiGenerated = false)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Comment body cannot be empty.", nameof(body));

        return new Comment
        {
            Body = body.Trim(),
            TicketId = ticketId,
            AuthorId = authorId,
            CreatedBy = createdBy,
            IsInternal = isInternal,
            IsAiGenerated = isAiGenerated
        };
    }
}

/// <summary>
/// WHY Attachment is separate from Comment:
/// Attachments are binary files stored in Azure Blob Storage.
/// We only store the BlobName (storage path) and BlobUrl (download link) here.
/// The actual file lives in Azure — this is standard cloud practice.
/// BlobUrl is a time-limited SAS URL generated on demand, not stored permanently.
/// </summary>
public class Attachment : BaseEntity
{
    public string FileName { get; private set; } = string.Empty;
    public string BlobName { get; private set; } = string.Empty;
    public string BlobUrl { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }

    public Guid TicketId { get; private set; }
    public Ticket? Ticket { get; private set; }

    private Attachment() { }

    public static Attachment Create(
        string fileName,
        string blobName,
        string blobUrl,
        string contentType,
        long fileSizeBytes,
        Guid ticketId,
        string createdBy)
    {
        return new Attachment
        {
            FileName = fileName,
            BlobName = blobName,
            BlobUrl = blobUrl,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            TicketId = ticketId,
            CreatedBy = createdBy
        };
    }
}
