using SmartDesk.API.Extensions;

/// <summary>
/// WHY SO SHORT?
/// All registration logic is in extension methods (ServiceExtensions.cs).
/// Program.cs should be a table of contents, not a wall of code.
/// This is the recommended pattern for .NET 8 minimal APIs.
/// </summary>

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSmartDeskInfrastructure(builder.Configuration)  // Cosmos, Redis, Blob, Service Bus
    .AddSmartDeskServices(builder.Configuration)         // TicketService, UserService
    .AddSmartDeskAuth(builder.Configuration)             // JWT + OAuth 2.0
    .AddSmartDeskSwagger()                               // Swagger UI + API docs
    .AddSmartDeskHealthChecks(builder.Configuration);    // /health endpoint

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();   // Must come before UseAuthorization
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapSmartDeskEndpoints();  // All Minimal API routes

app.Run();

// WHY THIS LINE?
// Makes Program class visible to SmartDesk.IntegrationTests project
// so it can create a WebApplicationFactory<Program> for integration testing.
public partial class Program { }
