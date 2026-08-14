# SmartDesk — AI-Powered IT Helpdesk

> Full-stack cloud-native helpdesk system built with ASP.NET Core 8, Blazor, and Azure.  
> Demonstrates Clean Architecture, event-driven AI processing, and enterprise DevOps practices.

![Build](https://github.com/HamzaAhmad416/SmartDesk/actions/workflows/ci.yml/badge.svg)

![.NET](https://img.shields.io/badge/.NET-8.0-purple)

![Azure](https://img.shields.io/badge/Azure-CosmosDB%20%7C%20Service%20Bus%20%7C%20Redis-blue)

![Blazor](https://img.shields.io/badge/Blazor-Server-purple)

---

## What It Does

SmartDesk is an IT helpdesk SaaS where:
- **Users** submit support tickets with title, description, and priority
- **Agents** are assigned tickets, post comments, and resolve issues
- **AI** (OpenAI GPT-4o-mini via Azure Function) auto-suggests replies and categorises tickets
- **Managers** view real-time dashboard stats with Radzen charts

---

## Architecture

```
┌─────────────────┐     HTTP      ┌──────────────────┐
│  Blazor Server  │ ────────────► │  ASP.NET Core 8  │
│  (Radzen UI)    │               │  Minimal API      │
└─────────────────┘               └────────┬─────────┘
                                           │
                    ┌──────────────────────┼──────────────────────┐
                    │                      │                       │
             ┌──────▼──────┐    ┌─────────▼──────┐    ┌─────────▼──────┐
             │  CosmosDB   │    │  Azure Service  │    │  Redis Cache   │
             │  (tickets,  │    │  Bus (events)   │    │  (stats, lists)│
             │   users)    │    └────────┬────────┘    └────────────────┘
             └─────────────┘            │
                                 ┌──────▼──────┐
                                 │Azure Function│
                                 │(AI Processor)│
                                 └──────┬───────┘
                                        │
                                 ┌──────▼──────┐
                                 │   OpenAI    │
                                 │ GPT-4o-mini │
                                 └─────────────┘
```

### Clean Architecture Layers

| Layer | Project | Responsibility |
|---|---|---|
| Domain | `SmartDesk.Domain` | Entities, business rules, enums |
| Application | `SmartDesk.Application` | Services, interfaces, DTOs |
| Infrastructure | `SmartDesk.Infrastructure` | CosmosDB, Redis, Service Bus, Blob Storage |
| API | `SmartDesk.API` | Minimal API endpoints, JWT auth, Swagger |
| UI | `SmartDesk.Blazor` | Blazor Server, Radzen components |
| Functions | `SmartDesk.Functions` | Azure Functions, AI processing |

---

## Tech Stack

**Backend**
- ASP.NET Core 8 Minimal API with API versioning (v1/v2)
- Clean Architecture + DDD (Domain-Driven Design)
- JWT + OAuth 2.0 authentication
- Rate limiting, OpenAPI/Swagger docs
- Polly retry + circuit-breaker resilience

**Frontend**
- Blazor Server with Radzen component library
- Radzen DataGrid, Charts, Dialogs, Notifications

**Azure Services**
- Azure CosmosDB — primary data store (tickets, users, comments)
- Azure Service Bus — async event publishing
- Azure Redis Cache — performance layer (10min TTL on stats)
- Azure Blob Storage — file attachments with SAS URLs
- Azure Functions — serverless AI processing
- Azure App Service — hosting
- Azure Key Vault — secrets management

**DevOps**
- GitHub Actions CI/CD (build → test → Docker → deploy)
- Docker multi-stage builds
- xUnit unit tests with Moq

---

## Getting Started Locally

### Prerequisites
- .NET 8 SDK
- Visual Studio 2022 or VS Code
- Azure subscription (or Azurite for local emulation)

### 1. Clone the repo
```bash
git clone https://github.com/HamzaAhmad416/SmartDesk.git
cd SmartDesk
```

### 2. Configure secrets
Copy `SmartDesk.API/appsettings.json` and fill in your values:
```json
{
  "ConnectionStrings": {
    "CosmosDb": "YOUR_COSMOS_CONNECTION_STRING",
    "Redis": "YOUR_REDIS_CONNECTION_STRING"
  },
  "Jwt": { "Key": "YOUR_SECRET_KEY_MIN_32_CHARS" },
  "AzureServiceBus": { "ConnectionString": "YOUR_SERVICE_BUS_CS" },
  "OpenAI": { "ApiKey": "YOUR_OPENAI_KEY" }
}
```

### 3. Run the API
```bash
cd SmartDesk.API
dotnet run
```
Swagger UI: `https://localhost:7000/swagger`

### 4. Run the Blazor UI
```bash
cd SmartDesk.Blazor
dotnet run
```
App: `https://localhost:7001`

### 5. Run tests
```bash
dotnet test SmartDesk.UnitTests/SmartDesk.UnitTests.csproj
```

---

## Key Features Explained

### AI Ticket Processing (Async)
1. User submits ticket → saved to CosmosDB in ~200ms
2. API publishes `ticket.created` event to Azure Service Bus
3. Azure Function wakes up, calls OpenAI GPT-4o-mini
4. AI generates a reply suggestion + categorises the ticket
5. Cosmos document is patched with AI results
6. Agent sees AI suggestion when viewing the ticket

### Redis Caching Strategy
- Dashboard stats: 10-minute TTL (expensive LINQ aggregation)
- Ticket detail: 5-minute TTL (invalidated on update/comment)
- Agent list: 5-minute TTL (invalidated on role change)
- Polly circuit breaker: if Redis fails, app reads from Cosmos (graceful degradation)

### API Versioning
- `/api/v1/tickets` — full CRUD
- `/api/v2/tickets` — adds filtering by status/priority + pagination

---

## Project Structure

```
SmartDesk/
├── SmartDesk.Domain/           # Entities, enums, base classes
├── SmartDesk.Application/      # Services, interfaces, DTOs
├── SmartDesk.Infrastructure/   # Azure SDK implementations
│   ├── Cosmos/                 # CosmosDB repositories + UnitOfWork
│   ├── Cache/                  # Redis + Polly
│   ├── Storage/                # Azure Blob Storage
│   └── Messaging/              # Azure Service Bus
├── SmartDesk.API/              # Minimal API + Swagger + JWT
├── SmartDesk.Blazor/           # Blazor Server + Radzen UI
├── SmartDesk.Functions/        # Azure Functions (AI processor)
└── SmartDesk.UnitTests/        # xUnit + Moq tests
```

---

## Author

**Hamza Ahmad** — Full Stack .NET Developer  
[LinkedIn](https://linkedin.com/in/hamza-ahmad-pk) · [GitHub](https://github.com/HamzaAhmad416)
