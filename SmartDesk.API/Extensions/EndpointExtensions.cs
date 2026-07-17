using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.DTOs;
using SmartDesk.Application.Services;

namespace SmartDesk.API.Extensions;

/// <summary>
/// WHY MINIMAL API instead of Controllers?
/// Minimal API (introduced .NET 6) is lighter — no controller classes needed.
/// Routes are defined as lambda functions grouped by MapGroup().
/// Result: less boilerplate, faster startup, easier to read.
/// Your CV lists "Minimal API" — this is the showcase.
///
/// WHY TWO VERSIONS (v1 and v2)?
/// API versioning is on your CV. V2 adds filtering + pagination on top of V1.
/// Real-world APIs version to avoid breaking existing consumers.
/// </summary>
public static class EndpointExtensions
{
    public static WebApplication MapSmartDeskEndpoints(this WebApplication app)
    {
        app.MapTicketsV1();
        app.MapTicketsV2();
        app.MapDashboard();
        app.MapUsers();
        return app;
    }

    // ── V1 Tickets ────────────────────────────────────────────────────────────

    private static void MapTicketsV1(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/tickets")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .WithTags("Tickets V1")
            .WithOpenApi();

        // GET all tickets
        group.MapGet("/", async (TicketService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAllAsync(ct)))
            .WithSummary("Get all tickets");

        // GET single ticket with full details (comments, attachments, AI reply)
        group.MapGet("/{id:guid}", async (Guid id, TicketService svc, CancellationToken ct) =>
        {
            var ticket = await svc.GetDetailAsync(id, ct);
            return ticket is null ? Results.NotFound() : Results.Ok(ticket);
        })
        .WithName("GetTicketV1")
        .WithSummary("Get ticket by ID");

        // POST create ticket
        // WHY extract claims from HttpContext?
        // The JWT token contains the user's ID and email — we read them here
        // so TicketService doesn't need to know about HTTP concepts.
        group.MapPost("/", async (
            [FromBody] CreateTicketRequest request,
            TicketService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = Guid.Parse(
                http.User.FindFirst("sub")?.Value ?? Guid.Empty.ToString());
            var email = http.User.FindFirst("email")?.Value ?? "unknown";

            var ticket = await svc.CreateAsync(request, userId, email, ct);
            return Results.CreatedAtRoute("GetTicketV1", new { id = ticket.Id }, ticket);
        })
        .WithSummary("Create a new ticket");

        // POST assign ticket to agent (Agent/Admin only)
        group.MapPost("/{id:guid}/assign", async (
            Guid id,
            [FromBody] AssignTicketRequest request,
            TicketService svc,
            CancellationToken ct) =>
        {
            await svc.AssignAsync(id, request.AgentId, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("AgentOrAdmin")
        .WithSummary("Assign ticket to an agent");

        // POST resolve ticket (Agent/Admin only)
        group.MapPost("/{id:guid}/resolve", async (
            Guid id,
            TicketService svc,
            CancellationToken ct) =>
        {
            await svc.ResolveAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("AgentOrAdmin")
        .WithSummary("Mark ticket as resolved");

        // POST add comment
        group.MapPost("/{id:guid}/comments", async (
            Guid id,
            [FromBody] CreateCommentRequest request,
            TicketService svc,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = Guid.Parse(
                http.User.FindFirst("sub")?.Value ?? Guid.Empty.ToString());
            var email = http.User.FindFirst("email")?.Value ?? "unknown";

            await svc.AddCommentAsync(id, request, userId, email, ct);
            return Results.NoContent();
        })
        .WithSummary("Add comment to ticket");
    }

    // ── V2 Tickets — adds filtering + pagination ───────────────────────────────

    private static void MapTicketsV2(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/tickets")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .WithTags("Tickets V2")
            .WithOpenApi();

        group.MapGet("/", async (
            [FromQuery] string? status,
            [FromQuery] string? priority,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            TicketService svc = default!,
            CancellationToken ct = default) =>
        {
            var all = await svc.GetAllAsync(ct);

            var filtered = all
                .Where(t => status is null ||
                    t.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase))
                .Where(t => priority is null ||
                    t.Priority.ToString().Equals(priority, StringComparison.OrdinalIgnoreCase))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Results.Ok(new
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = filtered.Count,
                Items = filtered
            });
        })
        .WithSummary("Get tickets with filtering and pagination (V2)");
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    private static void MapDashboard(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/dashboard")
            .RequireAuthorization("AgentOrAdmin")
            .WithTags("Dashboard")
            .WithOpenApi();

        group.MapGet("/stats", async (TicketService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDashboardStatsAsync(ct)))
            .WithSummary("Get dashboard stats (Redis-cached, 10 min TTL)");
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    private static void MapUsers(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/users")
            .RequireAuthorization()
            .WithTags("Users")
            .WithOpenApi();

        group.MapGet("/agents", async (UserService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAgentsAsync(ct)))
            .WithSummary("Get all agents (for assign dropdown)");

        group.MapPut("/{id:guid}/promote", async (
            Guid id,
            UserService svc,
            CancellationToken ct) =>
        {
            await svc.PromoteToAgentAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("AdminOnly")
        .WithSummary("Promote user to Agent role (Admin only)");
    }
}
