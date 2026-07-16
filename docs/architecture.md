# Garden Buddy – Architecture

## 1. Architecture Overview

Garden Buddy uses a client-server architecture.

```text
Angular Web Application
        |
        | HTTPS / JSON
        v
ASP.NET Core Web API
        |
        +-- Chat Orchestration
        |
        +-- EPAM DIAL API
        |
        +-- Controlled AI Tools
        |      |
        |      +-- Product Search Service
        |      +-- Product Details Service
        |      +-- Knowledge Search Service
        |
        +-- Entity Framework Core
        |      |
        |      +-- SQLite / SQL Database
        |
        +-- Embedding and Retrieval Services
               |
               +-- Knowledge Documents
               +-- Vector Index
```

## 2. Frontend

The frontend will be implemented as an Angular application.

Responsibilities:

- Display the chat interface
- Collect user messages
- Send requests to the backend
- Display chatbot answers
- Display answer sources
- Show loading and error states

The frontend will not:

- Store AI API keys
- Call the AI provider directly
- Execute business logic
- Access the database directly

## 3. Backend

The backend will be implemented using ASP.NET Core Web API.

Responsibilities:

- Validate API requests
- Coordinate chatbot interactions
- Call the AI provider
- Execute controlled application tools
- Query structured product data
- Retrieve unstructured knowledge
- Return answers and source information
- Handle errors and logging

## 4. Suggested Backend Layers

### API Layer

Contains:

- Controllers or endpoint definitions
- Request and response models
- API validation
- Swagger configuration
- Exception handling

### Application Layer

Contains:

- Chat orchestration
- Product search use cases
- Knowledge search use cases
- DTOs
- Interfaces for infrastructure services

### Domain Layer

Contains:

- Product entity
- Knowledge document concepts
- Domain rules where required

### Infrastructure Layer

Contains:

- Entity Framework Core
- Database configuration
- CSV import
- EPAM DIAL API integration
- Embedding generation
- Vector search implementation
- File-system document loading

### Tests

Contains:

- Unit tests
- Integration tests where practical
- Tool argument validation tests
- Product filtering tests

## 5. Structured Data Flow

Example question:

> Which beginner-friendly plants for a sunny balcony are currently in stock?

Flow:

1. The frontend sends the question to `POST /api/chat`.
2. The backend sends the question and tool definitions to the AI model.
3. The model requests the controlled `SearchProducts` tool.
4. The backend validates the tool arguments.
5. The Product Search Service creates a safe EF Core query.
6. The database returns matching products.
7. The backend sends the structured result back to the model.
8. The model generates a customer-friendly answer.
9. The API returns the answer and product sources.

## 6. Unstructured Data Flow

Example question:

> Do you offer home delivery?

Flow:

1. The frontend sends the question to the backend.
2. The model requests the `SearchKnowledgeBase` tool.
3. The backend generates or uses the question embedding.
4. The retrieval service finds the most relevant document chunks.
5. The retrieved text is returned to the model as grounded context.
6. The model creates an answer based only on the retrieved content.
7. The API returns the answer and document sources.

## 7. Mixed Data Flow

Example question:

> Which in-stock plants are suitable for a sunny balcony, and how should I care for them?

Flow:

1. The model calls `SearchProducts`.
2. The backend returns matching products.
3. The model calls `SearchKnowledgeBase` for relevant care guidance.
4. The backend returns matching document chunks.
5. The model combines the two grounded results.
6. The API returns the final answer with both product and document sources.

## 8. AI Tooling

The AI model will not access the database directly.

The backend exposes controlled functions such as:

- `SearchProducts`
- `GetProductDetails`
- `SearchKnowledgeBase`

Each tool will have:

- A clear description
- A strongly typed input model
- Validation
- Controlled application logic
- Safe database or retrieval access
- Structured output

## 9. Database

The initial PoC will use SQLite because it:

- Requires no separate database server
- Is easy to configure locally
- Works well with Entity Framework Core
- Can be replaced later if required

A future version may use PostgreSQL, SQL Server, or another approved relational database.

## 10. Retrieval-Augmented Generation

The RAG pipeline will:

1. Read Markdown documents.
2. Split them into smaller chunks.
3. Generate embeddings.
4. Store the chunks and embeddings.
5. Generate an embedding for the user query.
6. Find the most similar chunks.
7. Provide retrieved text to the AI model.
8. Return source metadata with the final answer.

The exact vector storage technology may be selected during implementation.

## 11. AI Provider Abstraction

The initial implementation will use the EPAM DIAL API.

The application design should avoid tightly coupling business logic to one provider.

This abstraction should only be introduced when needed and should not add unnecessary complexity to the first milestone.

### DIAL Core Request Contract

The backend service sends chat completion requests to:

- `POST /openai/deployments/{deployment_name}/chat/completions`
- Query parameter: `api-version=2024-10-21`

Request body format:

```json
{
  "model": "gpt-4",
  "messages": [
    { "role": "user", "content": "What are beginner-friendly plants?" }
  ],
  "temperature": 0.5,
  "top_p": 1,
  "max_tokens": 200
}
```

Authentication and headers:

- `Authorization: Bearer <DIAL_API_KEY>`
- `X-CACHE-POLICY: availability-priority`

Error handling maps provider failures into controlled backend responses for `400`, `401`, and `500` statuses.

### DIAL API Surface In Backend

The backend currently exposes:

- `POST /api/dial/chat-completion`
- `POST /api/dial/chat-completions`

Both endpoints are thin controller routes that delegate to the application DIAL service.

Tool-calling support:

- Request payload can include `tools` and `tool_choice`.
- Response payload supports assistant `tool_calls` for controlled backend tool execution orchestration.

## 12. API Contract

Initial chat endpoint:

```http
POST /api/chat
Content-Type: application/json
```

Example request:

```json
{
  "message": "Which beginner-friendly plants are currently in stock?"
}
```

Example response:

```json
{
  "answer": "Lavender and geranium are currently available and suitable for beginners.",
  "sources": [
    {
      "type": "product",
      "name": "Lavender"
    },
    {
      "type": "product",
      "name": "Geranium"
    }
  ]
}
```

## 13. Security

- Store secrets outside source control.
- Never expose AI credentials to Angular.
- Validate all public inputs.
- Validate AI-generated tool arguments.
- Do not execute arbitrary AI-generated SQL.
- Do not log secrets.
- Use only fictional or approved data.
- Return safe error responses.

## 14. Observability

The backend should log:

- Incoming request identifiers
- AI tool calls
- Tool execution duration
- Retrieval result counts
- Errors without sensitive data

Optional metrics may include:

- Chat request count
- Average response duration
- Tool usage frequency
- Empty-result frequency

## 15. Deployment

Production deployment is outside the initial PoC scope.

The local setup should support:

- Running the ASP.NET Core backend
- Running the Angular frontend
- Creating or seeding the SQLite database
- Loading sample documents
Configuring the EPAM DIAL API key through environment variables (e.g., `DIAL_API_KEY`)
