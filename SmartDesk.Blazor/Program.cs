using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using SmartDesk.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server with interactive server components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

/// <summary>
/// WHY AddRadzenComponents()?
/// Radzen components (DataGrid, Dialog, Notification, Charts) need their
/// services registered here. Without this, RadzenDialog and NotificationService
/// won't be injectable in components.
/// This is the standard Radzen setup for Blazor Server.
/// </summary>
builder.Services.AddRadzenComponents();

/// <summary>
/// WHY AddHttpClient typed client?
/// Creates a named HttpClient pre-configured with the API base URL.
/// Handles connection pooling correctly — never create HttpClient with 'new'.
/// BaseAddress points to our SmartDesk.API project.
/// </summary>
builder.Services.AddHttpClient<SmartDeskApiClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7000");
});

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
