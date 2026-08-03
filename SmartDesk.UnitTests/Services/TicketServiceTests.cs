using Microsoft.Extensions.Logging;
using Moq;
using SmartDesk.Application.DTOs;
using SmartDesk.Application.Interfaces;
using SmartDesk.Application.Services;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using Xunit;

namespace SmartDesk.UnitTests.Services;

/// <summary>
/// WHY MOCK DEPENDENCIES WITH MOQ?
/// TicketService depends on IUnitOfWork, IServiceBusPublisher, ICacheService.
/// We don't want real CosmosDB or Redis in unit tests — that would be slow,
/// brittle, and require Azure to be running.
///
/// Moq lets us create fake implementations:
/// - Mock<IUnitOfWork>().Setup(x => x.Tickets.GetByIdAsync(...)).ReturnsAsync(ticket)
/// - We control what the fake returns so we can test every code path
/// - Tests run in milliseconds, no network needed
///
/// This is WHY the Application layer depends on INTERFACES not concrete classes.
/// If TicketService directly used CosmosTicketRepository, we couldn't mock it.
/// </summary>
public class TicketServiceTests
{
    // ── Test Setup ────────────────────────────────────────────────────────────

    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<ITicketRepository> _ticketRepoMock = new();
    private readonly Mock<ICommentRepository> _commentRepoMock = new();
    private readonly Mock<IServiceBusPublisher> _busMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<ILogger<TicketService>> _loggerMock = new();

    private TicketService CreateSut()
    {
        // Wire up the UoW mock to return repo mocks
        _uowMock.Setup(u => u.Tickets).Returns(_ticketRepoMock.Object);
        _uowMock.Setup(u => u.Comments).Returns(_commentRepoMock.Object);

        // Default: cache always misses (returns null) so we test the real code path
        _cacheMock.Setup(c => c.GetAsync<TicketDetailDto>(It.IsAny<string>(), default))
                  .ReturnsAsync((TicketDetailDto?)null);
        _cacheMock.Setup(c => c.GetAsync<DashboardStatsDto>(It.IsAny<string>(), default))
                  .ReturnsAsync((DashboardStatsDto?)null);
        _cacheMock.Setup(c => c.GetAsync<IEnumerable<TicketListDto>>(It.IsAny<string>(), default))
                  .ReturnsAsync((IEnumerable<TicketListDto>?)null);

        return new TicketService(
            _uowMock.Object,
            _busMock.Object,
            _cacheMock.Object,
            _loggerMock.Object);
    }

    private static Ticket MakeTicket() =>
        Ticket.Create("PC broken", "Won't turn on", TicketPriority.High,
            Guid.NewGuid(), "user@test.com");

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_SavesTicketAndPublishesEvent()
    {
        var sut = CreateSut();
        var userId = Guid.NewGuid();
        var request = new CreateTicketRequest("PC broken", "Won't turn on", TicketPriority.High);

        // Arrange: GetWithDetailsAsync returns the saved ticket
        _ticketRepoMock
            .Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(MakeTicket());

        // Act
        var result = await sut.CreateAsync(request, userId, "user@test.com");

        // Assert: ticket was added to repo
        _ticketRepoMock.Verify(r => r.AddAsync(It.IsAny<Ticket>(), default), Times.Once);

        // Assert: changes were saved
        _uowMock.Verify(u => u.SaveChangesAsync(default), Times.Once);

        // Assert: Service Bus event was published (triggers AI Function)
        _busMock.Verify(b => b.PublishTicketCreatedAsync(It.IsAny<Guid>(), default), Times.Once);

        // Assert: dashboard cache was busted
        _cacheMock.Verify(c => c.RemoveAsync("dashboard:stats", default), Times.Once);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateAsync_EmptyTitle_ThrowsArgumentException()
    {
        var sut = CreateSut();

        // Domain entity validation kicks in before anything hits the database
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.CreateAsync(
                new CreateTicketRequest("", "Description", TicketPriority.Low),
                Guid.NewGuid(), "user@test.com"));
    }

    // ── GetDetailAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDetailAsync_CacheHit_ReturnsCachedValueWithoutHittingCosmos()
    {
        var sut = CreateSut();
        var ticketId = Guid.NewGuid();
        var cachedDto = new TicketDetailDto(ticketId, "Cached Title", "Desc",
            TicketStatus.Open, TicketPriority.Medium, TicketCategory.Software,
            "user", null, DateTime.UtcNow, null, null,
            Enumerable.Empty<CommentDto>(), Enumerable.Empty<AttachmentDto>());

        // Arrange: cache returns a hit
        _cacheMock
            .Setup(c => c.GetAsync<TicketDetailDto>($"ticket:{ticketId}", default))
            .ReturnsAsync(cachedDto);

        // Act
        var result = await sut.GetDetailAsync(ticketId);

        // Assert: returned from cache
        Assert.Equal("Cached Title", result?.Title);

        // Assert: Cosmos was never called (cache hit)
        _ticketRepoMock.Verify(
            r => r.GetWithDetailsAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task GetDetailAsync_CacheMiss_ReadsFromCosmosAndCaches()
    {
        var sut = CreateSut();
        var ticket = MakeTicket();

        // Arrange: cache miss, Cosmos returns ticket
        _ticketRepoMock
            .Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(ticket);

        // Act
        var result = await sut.GetDetailAsync(ticket.Id);

        // Assert: Cosmos was queried
        _ticketRepoMock.Verify(
            r => r.GetWithDetailsAsync(ticket.Id, default), Times.Once);

        // Assert: result was stored in Redis
        _cacheMock.Verify(
            c => c.SetAsync(
                $"ticket:{ticket.Id}",
                It.IsAny<TicketDetailDto>(),
                It.IsAny<TimeSpan?>(),
                default),
            Times.Once);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetDetailAsync_TicketNotFound_ReturnsNull()
    {
        var sut = CreateSut();

        _ticketRepoMock
            .Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Ticket?)null);

        var result = await sut.GetDetailAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // ── AssignAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AssignAsync_ValidTicket_AssignsAndPublishesEvent()
    {
        var sut = CreateSut();
        var ticket = MakeTicket();
        var agentId = Guid.NewGuid();

        _ticketRepoMock
            .Setup(r => r.GetByIdAsync(ticket.Id, default))
            .ReturnsAsync(ticket);

        await sut.AssignAsync(ticket.Id, agentId);

        // Domain rule was applied
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(agentId, ticket.AssignedAgentId);

        // Event published
        _busMock.Verify(
            b => b.PublishTicketAssignedAsync(ticket.Id, agentId, default),
            Times.Once);

        // Cache busted
        _cacheMock.Verify(
            c => c.RemoveAsync($"ticket:{ticket.Id}", default),
            Times.Once);
    }

    [Fact]
    public async Task AssignAsync_TicketNotFound_ThrowsKeyNotFoundException()
    {
        var sut = CreateSut();

        _ticketRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Ticket?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sut.AssignAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    // ── ResolveAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ValidTicket_ResolvesAndPublishesEvent()
    {
        var sut = CreateSut();
        var ticket = MakeTicket();

        _ticketRepoMock
            .Setup(r => r.GetByIdAsync(ticket.Id, default))
            .ReturnsAsync(ticket);

        await sut.ResolveAsync(ticket.Id);

        // Domain rule was applied
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.NotNull(ticket.ResolvedAt);

        // Event published
        _busMock.Verify(
            b => b.PublishTicketResolvedAsync(ticket.Id, default),
            Times.Once);

        // Both ticket cache and stats cache busted
        _cacheMock.Verify(c => c.RemoveAsync($"ticket:{ticket.Id}", default), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync("dashboard:stats", default), Times.Once);
    }

    // ── AddCommentAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task AddCommentAsync_ValidComment_SavesAndBustsCache()
    {
        var sut = CreateSut();
        var ticketId = Guid.NewGuid();
        var request = new CreateCommentRequest("Have you tried turning it off and on?");

        await sut.AddCommentAsync(ticketId, request, Guid.NewGuid(), "agent@test.com");

        // Comment was saved
        _commentRepoMock.Verify(
            r => r.AddAsync(It.IsAny<Comment>(), default),
            Times.Once);

        // Ticket detail cache busted so comment appears immediately
        _cacheMock.Verify(
            c => c.RemoveAsync($"ticket:{ticketId}", default),
            Times.Once);
    }

    // ── GetDashboardStatsAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetDashboardStatsAsync_NoTickets_ReturnsZeroStats()
    {
        var sut = CreateSut();

        _ticketRepoMock
            .Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(Enumerable.Empty<Ticket>());

        var stats = await sut.GetDashboardStatsAsync();

        Assert.Equal(0, stats.TotalOpen);
        Assert.Equal(0, stats.TotalResolved);
        Assert.Equal(0, stats.AvgResolutionHours);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_WithTickets_CalculatesCorrectly()
    {
        var sut = CreateSut();

        var openTicket = MakeTicket();
        var resolvedTicket = MakeTicket();
        resolvedTicket.Resolve();

        _ticketRepoMock
            .Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(new[] { openTicket, resolvedTicket });

        var stats = await sut.GetDashboardStatsAsync();

        Assert.Equal(1, stats.TotalOpen);
        Assert.Equal(1, stats.TotalResolved);
    }
}
