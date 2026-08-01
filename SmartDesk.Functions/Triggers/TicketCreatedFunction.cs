using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

namespace SmartDesk.Functions.Triggers;

/// <summary>
/// WHY AN AZURE FUNCTION FOR AI PROCESSING?
///
/// When a ticket is created in the API, it publishes "ticket.created" to Service Bus.
/// This Function wakes up automatically when that message arrives.
///
/// WHY NOT CALL OPENAI DIRECTLY IN THE API?
/// 1. OpenAI can take 2-5 seconds — user would wait for ticket creation
/// 2. If OpenAI is down, ticket creation would fail
/// 3. With Service Bus + Function: ticket creation is instant (~200ms)
///    AI processing happens in the background
///
/// WHY TWO SEPARATE FUNCTIONS?
/// Single Responsibility — one function categorises, one suggests a reply.
/// They can scale independently and fail independently.
///
/// HOW IT WORKS END TO END:
/// User submits ticket → API saves to Cosmos → publishes to Service Bus
/// → This Function fires → calls OpenAI → updates ticket in Cosmos
/// → Next time user views ticket, AI reply is there
/// </summary>
public class TicketCreatedFunction
{
    private readonly ILogger<TicketCreatedFunction> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _openAiKey;
    private readonly string _cosmosConnectionString;

    public TicketCreatedFunction(
        ILogger<TicketCreatedFunction> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration config)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _openAiKey = config["OpenAI:ApiKey"] ?? string.Empty;
        _cosmosConnectionString = config["ConnectionStrings:CosmosDb"] ?? string.Empty;
    }

    /// <summary>
    /// Triggered by Service Bus topic "ticket-events", subscription "ai-processor".
    /// The [ServiceBusTrigger] attribute tells Azure Functions to bind this method
    /// to incoming messages automatically — no polling code needed.
    /// </summary>
    [Function("TicketCreatedAiProcessor")]
    public async Task RunAsync(
        [ServiceBusTrigger(
            topicName: "ticket-events",
            subscriptionName: "ai-processor",
            Connection = "AzureServiceBus:ConnectionString")]
        string messageBody,
        FunctionContext context)
    {
        _logger.LogInformation("Processing ticket.created event: {Body}", messageBody);

        try
        {
            // Deserialize the message payload
            var payload = JsonSerializer.Deserialize<TicketCreatedPayload>(
                messageBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload is null)
            {
                _logger.LogWarning("Null payload received — skipping");
                return;
            }

            // 1. Fetch ticket from CosmosDB to get title + description
            var ticket = await GetTicketFromCosmosAsync(payload.TicketId);
            if (ticket is null)
            {
                _logger.LogWarning("Ticket {TicketId} not found in Cosmos", payload.TicketId);
                return;
            }

            // 2. Call OpenAI for reply suggestion + category in parallel
            var replyTask = GetAiReplyAsync(ticket.Title, ticket.Description);
            var categoryTask = GetAiCategoryAsync(ticket.Title, ticket.Description);
            await Task.WhenAll(replyTask, categoryTask);

            // 3. Update ticket in CosmosDB with AI results
            await UpdateTicketInCosmosAsync(
                payload.TicketId,
                replyTask.Result,
                categoryTask.Result);

            _logger.LogInformation(
                "AI processing complete for ticket {TicketId} — Category: {Category}",
                payload.TicketId, categoryTask.Result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI processing failed for message: {Body}", messageBody);
            throw; // Re-throw so Service Bus retries the message
        }
    }

    // ── OpenAI Calls ─────────────────────────────────────────────────────────

    private async Task<string> GetAiReplyAsync(string title, string description)
    {
        var prompt = $"""
            You are an IT helpdesk support agent. A user submitted the following ticket:
            
            Title: {title}
            Description: {description}
            
            Write a professional, helpful reply (3-5 sentences) acknowledging their issue
            and providing initial troubleshooting steps. Be concise and friendly.
            """;

        return await CallOpenAiAsync(prompt, maxTokens: 200);
    }

    private async Task<string> GetAiCategoryAsync(string title, string description)
    {
        var prompt = $"""
            Categorise this IT helpdesk ticket into EXACTLY ONE of these categories:
            Hardware, Software, Network, Security, Access, Other
            
            Title: {title}
            Description: {description}
            
            Reply with ONLY the category name, nothing else.
            """;

        var result = await CallOpenAiAsync(prompt, maxTokens: 10);
        return result.Trim();
    }

    private async Task<string> CallOpenAiAsync(string prompt, int maxTokens)
    {
        if (string.IsNullOrEmpty(_openAiKey))
            return "AI service not configured";

        var requestBody = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = maxTokens
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _openAiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "No response";
    }

    // ── CosmosDB Operations ───────────────────────────────────────────────────

    private async Task<TicketSnapshot?> GetTicketFromCosmosAsync(Guid ticketId)
    {
        using var cosmosClient = new Microsoft.Azure.Cosmos.CosmosClient(
            _cosmosConnectionString);
        var container = cosmosClient
            .GetContainer("SmartDeskDb", "tickets");

        try
        {
            var response = await container.ReadItemAsync<TicketSnapshot>(
                ticketId.ToString(),
                new Microsoft.Azure.Cosmos.PartitionKey(ticketId.ToString()));
            return response.Resource;
        }
        catch (Microsoft.Azure.Cosmos.CosmosException ex)
            when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task UpdateTicketInCosmosAsync(
        Guid ticketId, string aiReply, string category)
    {
        using var cosmosClient = new Microsoft.Azure.Cosmos.CosmosClient(
            _cosmosConnectionString);
        var container = cosmosClient
            .GetContainer("SmartDeskDb", "tickets");

        // Patch operation — update only the fields we care about
        // WHY PATCH instead of read-modify-write?
        // Patch is atomic and cheaper — only sends the changed fields to Cosmos
        var patchOperations = new List<Microsoft.Azure.Cosmos.PatchOperation>
        {
            Microsoft.Azure.Cosmos.PatchOperation.Set("/aiSuggestedReply", aiReply),
            Microsoft.Azure.Cosmos.PatchOperation.Set("/category", category),
            Microsoft.Azure.Cosmos.PatchOperation.Set("/updatedAt", DateTime.UtcNow)
        };

        await container.PatchItemAsync<object>(
            ticketId.ToString(),
            new Microsoft.Azure.Cosmos.PartitionKey(ticketId.ToString()),
            patchOperations);

        _logger.LogInformation("Ticket {TicketId} patched with AI results", ticketId);
    }
}

// ── DTOs for this Function ────────────────────────────────────────────────────

public record TicketCreatedPayload(Guid TicketId);

public class TicketSnapshot
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

// Minimal IConfiguration interface for Functions
public interface IConfiguration
{
    string? this[string key] { get; }
}
