using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using SmartDesk.Application.Interfaces;

namespace SmartDesk.Infrastructure.Messaging;

/// <summary>
/// WHY AZURE SERVICE BUS?
///
/// When a ticket is created, two things need to happen:
/// 1. AI generates a reply suggestion (calls OpenAI — can take 2-5 seconds)
/// 2. AI auto-categorises the ticket (another OpenAI call)
///
/// If we did this synchronously, the user would wait 5-10 seconds for ticket creation.
/// With Service Bus:
/// - Ticket is created and saved to Cosmos in ~200ms
/// - User gets a 201 response immediately
/// - Service Bus holds the "ticket.created" message
/// - Azure Function picks it up, calls OpenAI, updates the ticket async
/// - Next time user views the ticket, the AI reply is there
///
/// This is the event-driven architecture pattern.
/// It also means: if OpenAI is down, tickets still get created.
/// Messages queue up and process when OpenAI recovers.
///
/// ApplicationProperties["EventType"] on each message lets Azure Functions
/// use topic SUBSCRIPTIONS with filters — different functions handle
/// different event types on the same topic.
/// </summary>
public class ServiceBusPublisher : IServiceBusPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusPublisher> _logger;

    public ServiceBusPublisher(
        ServiceBusClient client,
        string topicName,
        ILogger<ServiceBusPublisher> logger)
    {
        // Sender is created once per topic and reused — it's thread-safe
        _sender = client.CreateSender(topicName);
        _logger = logger;
    }

    public Task PublishTicketCreatedAsync(Guid ticketId, CancellationToken ct = default)
        => PublishAsync("ticket.created", new { TicketId = ticketId }, ct);

    public Task PublishTicketAssignedAsync(Guid ticketId, Guid agentId, CancellationToken ct = default)
        => PublishAsync("ticket.assigned", new { TicketId = ticketId, AgentId = agentId }, ct);

    public Task PublishTicketResolvedAsync(Guid ticketId, CancellationToken ct = default)
        => PublishAsync("ticket.resolved", new { TicketId = ticketId }, ct);

    private async Task PublishAsync<T>(string eventType, T payload, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(payload);

        var message = new ServiceBusMessage(body)
        {
            MessageId = Guid.NewGuid().ToString(),
            Subject = eventType,
            ContentType = "application/json"
        };

        // This property is indexed — Azure Function subscriptions can filter on it
        // e.g. "SELECT * FROM messages WHERE EventType = 'ticket.created'"
        message.ApplicationProperties["EventType"] = eventType;

        await _sender.SendMessageAsync(message, ct);
        _logger.LogInformation("Published '{EventType}' — {Body}", eventType, body);
    }

    // Service Bus sender holds a network connection — always dispose properly
    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
