# Garden Buddy – Coding Agent Instructions

## Project Context

Garden Buddy is a Proof of Concept AI chatbot for a fictional garden center named Green Oasis.

Read these files before proposing or implementing changes:

- `README.md`
- `docs/concept.md`
- `docs/architecture.md`

## Required Technology Stack

### Backend

- C#
- ASP.NET Core Web API
- Entity Framework Core
- SQLite for the initial implementation
- EPAM DIAL API integration
- Swagger / OpenAPI
- xUnit

### Frontend

- Angular
- TypeScript
- Standalone Angular components
- Angular HttpClient

Do not place AI credentials or AI orchestration logic in the frontend.

## Architecture Rules

- Keep controllers thin.
- Keep business logic in application services.
- Keep domain entities independent from infrastructure concerns where practical.
- Use dependency injection throughout the backend.
- Use asynchronous APIs for database and external service calls.
- Prefer simple, maintainable code over unnecessary framework abstractions.
- Do not introduce MediatR, CQRS, event sourcing, or microservices unless explicitly requested.
- Do not place database queries directly in controllers.
- Do not allow the language model to execute arbitrary SQL.
- Expose only controlled backend functions to the AI model.
- Validate all AI tool arguments before executing application logic.

## AI Behavior

The chatbot must:

- Use structured product data for prices, stock, categories, and product properties.
- Use unstructured documents for policies, FAQs, and gardening guidance.
- Use tool calling for controlled backend operations.
- State clearly when the available data does not contain an answer.
- Never fabricate prices, stock availability, policies, or product details.
- Include source metadata in chatbot responses where practical.

## Initial AI Tools

Implement the following capabilities incrementally:

### SearchProducts

Search products by:

- Name
- Category
- Sunlight requirement
- Difficulty
- Minimum price
- Maximum price
- Stock availability

### GetProductDetails

Return:

- Product name
- Description
- Price
- Current stock
- Sunlight requirement
- Watering requirement
- Difficulty
- Other relevant care information

### SearchKnowledgeBase

Search:

- Frequently asked questions
- Opening hours
- Payment methods
- Delivery policy
- Returns policy
- General plant-care guidance

### Mixed Queries

Support questions that require both product data and document knowledge.

Example:

> Which beginner-friendly plants for a sunny balcony are currently in stock, and how should I care for them?

## Security Rules

- Never commit API keys, passwords, tokens, or secrets.
- Read the EPAM DIAL API key from configuration or the `DIAL_API_KEY` environment variable.
- Add example configuration files without real secrets.
- Validate all public API inputs.
- Do not expose stack traces or internal configuration to the frontend.
- Do not log sensitive credentials.
- Use only fictional or approved data.

## Testing Rules

Add automated tests for:

- Product filtering
- Stock filtering
- Price filtering
- Empty search results
- Invalid API requests
- AI tool argument validation
- Missing AI configuration
- Knowledge retrieval behavior where practical

Before completing a backend task, run:

```bash
dotnet build
dotnet test
```

Before completing a frontend task, run the relevant Angular build and tests.

## Working Process

For every implementation task:

1. Inspect the repository.
2. Read the project documentation.
3. Summarize the intended change.
4. Implement one coherent milestone only.
5. Build the affected projects.
6. Run relevant automated tests.
7. Report changed files, decisions, limitations, and next steps.

Do not rewrite unrelated files.

## Planned Milestones

### Milestone 1

Status: Done

- Create the .NET solution.
- Create API, Application, Domain, Infrastructure, and Tests projects.
- Configure EF Core and SQLite.
- Define the Product entity.
- Import or seed products from CSV.
- Implement product search without AI.
- Add tests.

### Milestone 2

Status: Done

- Import sample product data.
- Implement product search without AI.
- Add product filtering tests.

### Milestone 3

Status: Done

- Integrate the EPAM DIAL API.
- Implement controlled tool calling.
- Expose product search as an AI tool.
- Add `POST /api/chat`.
- Return source information.
- Add validation, logging, and tests.

Current implementation notes:

- DIAL chat completion backend routes: `POST /api/dial/chat-completion` and `POST /api/dial/chat-completions`.
- Provider target: `/openai/deployments/{deployment_name}/chat/completions?api-version=2024-10-21`.
- Auth: `DIAL_API_KEY` environment variable has priority over `Dial:ApiKey` config fallback.
- Request and response contracts include optional tool-calling payloads (`tools`, `tool_choice`, `parallel_tool_calls`, `tool_calls`).

### Milestone 4

Status: Planned

- Ingest Markdown documents.
- Split documents into searchable chunks.
- Generate embeddings.
- Implement semantic knowledge retrieval.
- Expose knowledge search as an AI tool.

### Milestone 5

Status: Planned

- Support mixed structured and unstructured questions.
- Create the Angular chat frontend.
- Display answer sources.
- Add loading and error states.

### Milestone 6

Status: Planned

- Integrate the frontend with backend chat APIs.
- Refine user experience and error handling.

### Milestone 7

Status: Planned

- Finalize tests.
- Complete architecture documentation.
- Prepare the final one-pager.
- Prepare the five-minute demo.
- Document lessons learned.
