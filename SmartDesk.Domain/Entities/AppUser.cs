using SmartDesk.Domain.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

/// <summary>
/// WHY AppUser and not just "User"?
/// "User" conflicts with System.Security.Principal.IIdentity in some contexts.
/// "AppUser" is unambiguous and a common .NET convention.
///
/// WHY store users in Cosmos instead of ASP.NET Identity?
/// ASP.NET Identity is opinionated and SQL-first. Since we're using CosmosDB
/// as our primary store and authenticating via Azure AD / JWT, we just mirror
/// the user profile here. Simpler, and matches what you'd do in real cloud apps.
/// </summary>
public class AppUser : BaseEntity
{
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; } = UserRole.User;
    public bool IsActive { get; private set; } = true;

    // Azure AD Object ID — used to match incoming JWT tokens to our user record
    public string? AzureAdObjectId { get; private set; }

    // Navigation
    public ICollection<Ticket> SubmittedTickets { get; private set; } = new List<Ticket>();
    public ICollection<Ticket> AssignedTickets { get; private set; } = new List<Ticket>();

    private AppUser() { }

    public static AppUser Create(
        string email,
        string displayName,
        UserRole role = UserRole.User,
        string? azureAdObjectId = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty.", nameof(email));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty.", nameof(displayName));

        return new AppUser
        {
            Email = email.ToLowerInvariant().Trim(),
            DisplayName = displayName.Trim(),
            Role = role,
            AzureAdObjectId = azureAdObjectId,
            CreatedBy = email
        };
    }

    public void Promote(UserRole newRole)
    {
        Role = newRole;
        SetUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdated();
    }
}
