#  SmartDesk-Personal-Project
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


Clean Architecture Layers
Layer	Project	Responsibility
Domain	SmartDesk.Domain	Entities, business rules, enums
Application	SmartDesk.Application	Services, interfaces, DTOs
Infrastructure	SmartDesk.Infrastructure	CosmosDB, Redis, Service Bus, Blob Storage
API	SmartDesk.API	Minimal API endpoints, JWT auth, Swagger
UI	SmartDesk.Blazor	Blazor Server, Radzen components
Functions	SmartDesk.Functions	Azure Functions, AI processing
Tech Stack

Backend

ASP.NET Core 8 Minimal API with API versioning (v1/v2)
Clean Architecture + DDD (Domain-Driven Design)
JWT + OAuth 2.0 authentication
Rate limiting, OpenAPI/Swagger docs
Polly retry + circuit-breaker resilience

Frontend

Blazor Server with Radzen component library
Radzen DataGrid, Charts, Dialogs, Notifications

Azure Services

Azure CosmosDB — primary data store (tickets, users, comments)
Azure Service Bus — async event publishing
Azure Redis Cache — performance layer (10min TTL on stats)
Azure Blob Storage — file attachments with SAS URLs
Azure Functions — serverless AI processing
Azure App Service — hosting
Azure Key Vault — secrets management

DevOps

GitHub Actions CI/CD (build → test → Docker → deploy)
Docker multi-stage builds
xUnit unit tests with Moq
