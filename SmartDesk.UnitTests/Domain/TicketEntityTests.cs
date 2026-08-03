using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using Xunit;

namespace SmartDesk.UnitTests.Domain;

/// <summary>
/// WHY TEST DOMAIN ENTITIES FIRST?
/// Domain entities contain your core business rules.
/// If Ticket.Resolve() doesn't set ResolvedAt, your whole app is wrong.
/// These tests are the cheapest to write and catch the most critical bugs.
///
/// WHY NO MOCKS HERE?
/// Domain entities have zero dependencies — no database, no HTTP, no Azure.
/// Tests are pure C# — fast, simple, always reliable.
/// This is the biggest benefit of Clean Architecture + DDD.
/// </summary>
public class TicketEntityTests
{
    private static Ticket CreateValidTicket() =>
        Ticket.Create(
            "PC won't boot",
            "Pressing power button, nothing happens.",
            TicketPriority.High,
            Guid.NewGuid(),
            "user@test.com");

    // ── Creation ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ValidInputs_ReturnsTicketWithOpenStatus()
    {
        var ticket = CreateValidTicket();

        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(TicketCategory.Uncategorised, ticket.Category);
        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.NotEqual(Guid.Empty, ticket.Id);
    }

    [Fact]
    public void Create_SetsCreatedAtToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var ticket = CreateValidTicket();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(ticket.CreatedAt, before, after);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyTitle_ThrowsArgumentException(string title)
    {
        // Business rule: ticket must have a title
        Assert.Throws<ArgumentException>(() =>
            Ticket.Create(title, "Description", TicketPriority.Low,
                Guid.NewGuid(), "user@test.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyDescription_ThrowsArgumentException(string description)
    {
        Assert.Throws<ArgumentException>(() =>
            Ticket.Create("Title", description, TicketPriority.Low,
                Guid.NewGuid(), "user@test.com"));
    }

    // ── Assignment ────────────────────────────────────────────────────────────

    [Fact]
    public void Assign_SetsAgentIdAndChangesStatusToInProgress()
    {
        var ticket = CreateValidTicket();
        var agentId = Guid.NewGuid();

        ticket.Assign(agentId);

        // Business rule: assigning a ticket automatically moves it to InProgress
        Assert.Equal(agentId, ticket.AssignedAgentId);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.NotNull(ticket.UpdatedAt);
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_SetsStatusAndStampsResolvedAt()
    {
        var ticket = CreateValidTicket();
        var before = DateTime.UtcNow.AddSeconds(-1);

        ticket.Resolve();

        // Business rule: resolved ticket must have ResolvedAt timestamp
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.NotNull(ticket.ResolvedAt);
        Assert.True(ticket.ResolvedAt > before);
    }

    [Fact]
    public void ResolutionTime_ResolvedTicket_ReturnsCorrectDuration()
    {
        var ticket = CreateValidTicket();
        ticket.Resolve();

        // ResolutionTime should be a very small positive duration
        Assert.NotNull(ticket.ResolutionTime);
        Assert.True(ticket.ResolutionTime!.Value.TotalSeconds >= 0);
    }

    [Fact]
    public void ResolutionTime_OpenTicket_ReturnsNull()
    {
        var ticket = CreateValidTicket();

        // Open tickets have no resolution time yet
        Assert.Null(ticket.ResolutionTime);
    }

    // ── Category ─────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateCategory_ChangesCategory()
    {
        var ticket = CreateValidTicket();

        ticket.UpdateCategory(TicketCategory.Network);

        Assert.Equal(TicketCategory.Network, ticket.Category);
        Assert.NotNull(ticket.UpdatedAt);
    }

    // ── AI Reply ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetAiSuggestedReply_StoresReply()
    {
        var ticket = CreateValidTicket();
        const string reply = "Thank you for contacting IT support...";

        ticket.SetAiSuggestedReply(reply);

        Assert.Equal(reply, ticket.AiSuggestedReply);
        Assert.NotNull(ticket.UpdatedAt);
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Close_SetsStatusToClosed()
    {
        var ticket = CreateValidTicket();
        ticket.Resolve();
        ticket.Close();

        Assert.Equal(TicketStatus.Closed, ticket.Status);
    }
}
