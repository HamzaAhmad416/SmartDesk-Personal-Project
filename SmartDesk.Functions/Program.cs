using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// WHY ConfigureFunctionsWorkerDefaults()?
/// This sets up the isolated worker model for Azure Functions.
/// "Isolated" means the Function runs in its own .NET process,
/// separate from the Azure Functions host. This gives full .NET 8 support,
/// custom middleware, and proper DI — exactly like a regular .NET app.
/// 
/// The alternative (in-process) is being deprecated by Microsoft.
/// Isolated worker is the modern, recommended approach.
/// </summary>
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // HttpClient for OpenAI API calls
        // WHY IHttpClientFactory instead of new HttpClient()?
        // Factory manages connection pooling and avoids socket exhaustion
        // — a common production bug when creating HttpClient with 'new'.
        services.AddHttpClient();
    })
    .Build();

host.Run();
