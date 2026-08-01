using System.Net.Http.Json;
using SmartDesk.Application.DTOs;

namespace SmartDesk.Blazor.Services;

/// <summary>
/// WHY A TYPED HTTP CLIENT?
/// Instead of injecting raw HttpClient everywhere and repeating the base URL,
/// headers, and error handling in every component, we wrap all API calls here.
/// Blazor components inject SmartDeskApiClient and call clean methods like
/// GetTicketsAsync() instead of dealing with HTTP directly.
/// 
/// Registered via AddHttpClient<SmartDeskApiClient> in Program.cs which
/// handles lifetime, connection pooling, and base URL automatically.
/// </summary>
public class SmartDeskApiClient
{
    private readonly HttpClient _http;

    public SmartDeskApiClient(HttpClient http)
    {
        _http = http;
    }

    // ── Tickets ───────────────────────────────────────────────────────────────

    public async Task<List<TicketListDto>> GetTicketsAsync()
    {
        return await _http.GetFromJsonAsync<List<TicketListDto>>("/api/v1/tickets")
               ?? new List<TicketListDto>();
    }

    public async Task<TicketDetailDto?> GetTicketAsync(Guid id)
    {
        return await _http.GetFromJsonAsync<TicketDetailDto>($"/api/v1/tickets/{id}");
    }

    public async Task<TicketDetailDto?> CreateTicketAsync(CreateTicketRequest request)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/tickets", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TicketDetailDto>();
    }

    public async Task AssignTicketAsync(Guid ticketId, Guid agentId)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/v1/tickets/{ticketId}/assign",
            new AssignTicketRequest(agentId));
        response.EnsureSuccessStatusCode();
    }

    public async Task ResolveTicketAsync(Guid ticketId)
    {
        var response = await _http.PostAsync(
            $"/api/v1/tickets/{ticketId}/resolve", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task AddCommentAsync(Guid ticketId, CreateCommentRequest request)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/v1/tickets/{ticketId}/comments", request);
        response.EnsureSuccessStatusCode();
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    public async Task<DashboardStatsDto?> GetDashboardStatsAsync()
    {
        return await _http.GetFromJsonAsync<DashboardStatsDto>("/api/v1/dashboard/stats");
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<List<UserDto>> GetAgentsAsync()
    {
        return await _http.GetFromJsonAsync<List<UserDto>>("/api/v1/users/agents")
               ?? new List<UserDto>();
    }
}
