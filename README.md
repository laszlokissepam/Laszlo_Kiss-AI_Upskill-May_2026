# Garden Buddy

Garden Buddy is a Proof of Concept AI chatbot created for the EPAM AI Upskill Program.

The application supports customers of a fictional garden center called **Green Oasis**. It answers questions using structured product data, unstructured knowledge documents, and an AI model capable of calling controlled backend tools.

## Project Goals

The PoC should demonstrate how an AI chatbot can:

- Answer fact-based questions about products, prices, and stock.
- Answer policy and gardening questions from unstructured documents.
- Recommend suitable products based on customer requirements.
- Combine structured and unstructured data in a single answer.
- Avoid inventing information when no supporting data is available.

## Planned Technology Stack

### AI Provider

- EPAM DIAL API

### Backend

- C#
- ASP.NET Core Web API
- Entity Framework Core
- SQLite for the initial PoC
- EPAM DIAL API
- Swagger / OpenAPI
- xUnit

### Frontend

- Angular
- TypeScript
- Angular HttpClient
- Standalone components

### Data

- CSV product data imported into SQL
- Markdown documents for FAQs, store policies, and plant-care guidance
- Embeddings and vector search for Retrieval-Augmented Generation

## Planned Repository Structure

```text
garden-buddy/
├── AGENTS.md
├── README.md
├── docs/
│   ├── concept.md
│   └── architecture.md
├── backend/
├── frontend/
└── data/
```

The `backend`, `frontend`, and `data` folders will be created during implementation.

## Milestone Status

1. [Done] Create the ASP.NET Core solution and SQL data layer.
2. [Done] Import sample product data and implement product search.
3. [Done] Integrate the EPAM DIAL API and tool calling.
4. [Done] Add document ingestion, embeddings, and semantic retrieval.
5. [Planned] Add mixed structured and unstructured question support.
6. [Planned] Create a simple Angular chat interface.
7. [Planned] Add automated tests, documentation, and final demo materials.

## Local Development

Setup and run instructions will be added after the project skeleton has been implemented.

## EPAM DIAL Integration

The backend integrates with the DIAL Core chat completion endpoint:

- Base URL: `https://dialx.ai/api/v1`
- Endpoint: `POST /openai/deployments/{deployment_name}/chat/completions`
- Required query parameter: `api-version=2024-10-21`
- Default header: `X-CACHE-POLICY: availability-priority`
- Authentication: `Authorization: Bearer <token>` where token is resolved from `DIAL_API_KEY` (or `Dial:ApiKey` fallback)

Configuration is read from the `Dial` section in `backend/src/GardenBuddy.Api/appsettings*.json`.

Backend API endpoints:

- `POST /api/dial/chat-completion`
- `POST /api/dial/chat-completions`

Both routes forward requests to the DIAL chat completion API and support optional tool-calling fields (`tools`, `tool_choice`, `parallel_tool_calls`) for controlled backend function workflows.

Request validation and failure handling:

- Validates deployment name, message presence, temperature range, max token range, and tool definition schema.
- Supports `tool_choice` literals (`auto`, `none`, `required`) and specific function targeting via object shape.
- Enforces tool definition limits (maximum 128 tools) and validates tool schema consistency.
- Resolves API key from `DIAL_API_KEY` first, then falls back to `Dial:ApiKey`.
- Maps provider failures (`400`, `401`, `500`) into controlled API errors.

## Knowledge Retrieval API

Semantic retrieval is available through dedicated backend endpoints:

- `POST /api/knowledge/ingest`
- `POST /api/knowledge/search`

`/api/knowledge/ingest` reads Markdown files from `data/knowledge`, splits them into reusable chunks, and generates embeddings using the DIAL embedding endpoint.

`/api/knowledge/search` generates a query embedding and returns top-ranked chunks by cosine similarity.

Example search request:

```json
{
	"query": "Do you offer home delivery?",
	"topK": 3
}
```

Sample environment setup:

```powershell
$env:DIAL_API_KEY="your-token"
```

## Security

- API keys must never be committed to the repository.
- AI credentials must be stored in environment variables or secure configuration.
- The Angular frontend must never contain AI credentials.
- The language model must not generate or execute arbitrary SQL.
- Only fictional or otherwise approved data should be used in the PoC.
