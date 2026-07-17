using Microsoft.Extensions.Logging;
using SmartDesk.Application.DTOs;
using SmartDesk.Application.Interfaces;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Services;

/// <summary>
/// WHY UserService is separate from TicketService:
/// Single Responsibility Principle — each service owns one area.
/// TicketService owns ticket lifecycle.
/// UserService owns user registration, roles, and agent management.
///
/// GetOrCreateAsync is the key method here — called on every login.
/// When a user logs in via JWT/OAuth, we check if they exist in Cosmos.
/// If not, we create them automatically. This is "auto-registration" —
/// no separate sign-up flow needed.
/// </summary>
public class UserService
{
    private readonly IUnitOfWork _uow;
    private readonly ICacheService _cache;
    private readonly ILogger<UserService> _logger;

    private const string AgentsCacheKey = "users:agents";

    public UserService(IUnitOfWork uow, ICacheService cache, ILogger<UserService> logger)
    {
        _uow = uow;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Called on first login. Finds existing user by Azure AD ID or email,
    /// or creates a new one. Returns the user DTO either way.
    /// </summary>
    public async Task<UserDto> GetOrCreateAsync(
        string email,
        string displayName,
        string? azureObjectId = null,
        CancellationToken ct = default)
    {
        // Try Azure AD ID first (most reliable — email can change)
        var existing = azureObjectId is not null
            ? await _uow.Users.GetByAzureIdAsync(azureObjectId, ct)
            : await _uow.Users.GetByEmailAsync(email, ct);

        if (existing is not null)
            return MapToDto(existing);

        // First time this user logs in — create their profile
        var user = AppUser.Create(email, displayName, UserRole.User, azureObjectId);
        await _uow.Users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("New user auto-registered: {Email}", email);
        return MapToDto(user);
    }

    /// <summary>
    /// Gets all agents — used in the Blazor "Assign Ticket" dropdown.
    /// Cached 5 minutes since agent list rarely changes.
    /// </summary>
    public async Task<IEnumerable<UserDto>> GetAgentsAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<IEnumerable<UserDto>>(AgentsCacheKey, ct);
        if (cached is not null) return cached;

        var agents = await _uow.Users.GetAgentsAsync(ct);
        var dtos = agents.Select(MapToDto).ToList();

        await _cache.SetAsync(AgentsCacheKey, dtos, TimeSpan.FromMinutes(5), ct);
        return dtos;
    }

    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(id, ct);
        return user is null ? null : MapToDto(user);
    }

    public async Task PromoteToAgentAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _uow.Users.GetByIdAsync(userId, ct)
                   ?? throw new KeyNotFoundException($"User {userId} not found.");

        user.Promote(UserRole.Agent);
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(ct);

        // Invalidate agents cache — new agent must appear in dropdown
        await _cache.RemoveAsync(AgentsCacheKey, ct);
        _logger.LogInformation("User {UserId} promoted to Agent", userId);
    }

    private static UserDto MapToDto(AppUser u) =>
        new(u.Id, u.Email, u.DisplayName, u.Role);
}
