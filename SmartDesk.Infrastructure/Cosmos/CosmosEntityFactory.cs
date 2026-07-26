using System.Reflection;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Infrastructure.Cosmos;

/// <summary>
/// WHY REFLECTION HERE?
///
/// Domain entities have private setters to enforce business rules.
/// e.g. you can't do: ticket.Status = TicketStatus.Resolved (compile error)
/// You must call: ticket.Resolve() which also stamps ResolvedAt.
///
/// But when READING from CosmosDB, we're reconstructing a ticket that
/// already happened — we don't want to re-run business logic, we just
/// want to restore the exact saved state.
///
/// Reflection lets us bypass private setters ONLY at this boundary.
/// This is an accepted DDD pattern. The key rule: ONLY Infrastructure
/// uses this factory — Application and Domain never touch it.
///
/// Alternative: add internal setters + [assembly: InternalsVisibleTo]
/// But reflection is simpler for a portfolio project.
/// </summary>
public static class CosmosEntityFactory
{
    public static Ticket CreateTicket(
        Guid id, string title, string description,
        TicketStatus status, TicketPriority priority, TicketCategory category,
        Guid submittedByUserId, Guid? assignedAgentId,
        DateTime createdAt, DateTime? updatedAt, DateTime? resolvedAt,
        string createdBy, string? aiSuggestedReply)
    {
        // Creates an uninitialized Ticket WITHOUT calling any constructor
        var ticket = (Ticket)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(Ticket));

        Set(ticket, "Id", id);
        Set(ticket, "Title", title);
        Set(ticket, "Description", description);
        Set(ticket, "Status", status);
        Set(ticket, "Priority", priority);
        Set(ticket, "Category", category);
        Set(ticket, "SubmittedByUserId", submittedByUserId);
        Set(ticket, "AssignedAgentId", assignedAgentId);
        Set(ticket, "CreatedAt", createdAt);
        Set(ticket, "UpdatedAt", updatedAt);
        Set(ticket, "ResolvedAt", resolvedAt);
        Set(ticket, "CreatedBy", createdBy);
        Set(ticket, "AiSuggestedReply", aiSuggestedReply);

        // Initialize collections so null reference exceptions don't happen
        Set(ticket, "Comments", new List<Comment>());
        Set(ticket, "Attachments", new List<Attachment>());

        return ticket;
    }

    public static AppUser CreateUser(
        Guid id, string email, string displayName,
        UserRole role, bool isActive,
        string? azureAdObjectId, DateTime createdAt, string createdBy)
    {
        var user = (AppUser)System.Runtime.CompilerServices
    .RuntimeHelpers.GetUninitializedObject(typeof(AppUser));

        Set(user, "Id", id);
        Set(user, "Email", email);
        Set(user, "DisplayName", displayName);
        Set(user, "Role", role);
        Set(user, "IsActive", isActive);
        Set(user, "AzureAdObjectId", azureAdObjectId);
        Set(user, "CreatedAt", createdAt);
        Set(user, "CreatedBy", createdBy);
        Set(user, "SubmittedTickets", new List<Ticket>());
        Set(user, "AssignedTickets", new List<Ticket>());

        return user;
    }

    // Sets a property by name regardless of its access modifier
    private static void Set(object obj, string propertyName, object? value)
    {
        var prop = obj.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        prop?.SetValue(obj, value);
    }
}
