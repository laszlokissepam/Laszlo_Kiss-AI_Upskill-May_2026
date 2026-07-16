# Garden Buddy – Proof of Concept

## 1. Overview

Garden Buddy is an AI-powered chatbot for a fictional garden center named **Green Oasis**.

The chatbot will be integrated into a simple web application and will help customers find products, understand product availability, receive basic gardening guidance, and obtain answers to frequently asked questions.

The Proof of Concept will demonstrate how an AI assistant can use both structured and unstructured data while keeping the final answers grounded in available business information.

## 2. Target Users

The expected users are online and in-store customers of the garden center, including:

- Beginner gardeners
- Hobby gardeners
- Balcony gardeners
- Homeowners with small gardens
- Experienced customers looking for specific products or care guidance

## 3. Primary User Scenarios

### Beginner Gardener

A new gardener wants to plant flowers on a sunny balcony but does not know which plants, soil, or care routine to choose.

Example question:

> I want to plant flowers on my sunny balcony. What do you recommend for a beginner?

### Product Search

A customer wants to know whether a particular product is currently available.

Example question:

> Do you have lemon trees in stock?

### Price Inquiry

A customer wants to compare products or check a price.

Example question:

> How much does a 50-liter bag of potting soil cost?

### Gardening Problem

A customer needs help with a common gardening issue.

Example question:

> What is an organic way to get rid of aphids on roses?

### Product Care

A customer wants care instructions for a purchased plant.

Example question:

> How often should lavender be watered?

### Store Policy

A customer wants information about opening hours, payment, delivery, or returns.

Example question:

> Do you offer home delivery?

## 4. Problem Statement

Garden center employees spend a significant amount of time answering repetitive questions by phone, email, and in person.

Customers often expect immediate answers, but expert staff are not always available. This can result in:

- Lost sales opportunities
- Longer waiting times
- Repetitive workload for employees
- Inconsistent answers
- Reduced customer satisfaction

## 5. Expected Value

### Improved Customer Experience

Customers receive immediate answers at any time.

### Increased Staff Efficiency

Employees spend less time answering repetitive questions and can focus on complex or high-value customer interactions.

### Increased Sales Potential

The chatbot can recommend relevant products that match the customer's requirements and are currently in stock.

### Consistent Information

Answers are generated from approved product data and knowledge documents.

### Demonstration of AI Capabilities

The PoC demonstrates tool calling, SQL data access, embeddings, Retrieval-Augmented Generation, and mixed-data reasoning.

## 6. Planned Capabilities

### Structured Data Questions

The chatbot will answer questions using a relational product database.

Examples:

- Are lemon trees in stock?
- What is the price of potting soil?
- Which plants are suitable for shade?
- Which beginner-friendly plants cost less than a specified amount?
- Which suitable product is currently the cheapest?

### Unstructured Data Questions

The chatbot will answer questions using indexed Markdown documents.

Examples:

- What are the opening hours?
- Which payment methods are accepted?
- Is home delivery available?
- What is the return policy?
- How should lavender be cared for?

### Mixed Questions

As a more advanced capability, the chatbot will combine structured product data with unstructured knowledge.

Examples:

- Which in-stock plants are suitable for a sunny balcony, and how should I care for them?
- Which organic aphid treatment is available, and how should it be used?
- Which beginner-friendly plant fits my budget and the store's care recommendations?

## 7. Data Inputs

### Structured Data

Product data will be provided in a CSV file and imported into a relational SQL database.

Expected fields include:

- Product ID
- Name
- Category
- Description
- Price
- Stock quantity
- Sunlight requirement
- Watering requirement
- Difficulty
- Indoor or outdoor suitability
- Perennial flag
- Pet safety information

### Unstructured Data

The knowledge base will contain Markdown documents such as:

- Frequently asked questions
- Opening hours
- Payment policy
- Delivery policy
- Returns policy
- General gardening tips
- Plant-care guides

## 8. Planned Technology Stack

### Backend

- ASP.NET Core Web API
- C#
- Entity Framework Core
- SQLite for the initial PoC
- EPAM DIAL API integration
- Swagger / OpenAPI
- xUnit

### Frontend

- Angular
- TypeScript
- Angular HttpClient

### AI and Retrieval

- EPAM DIAL API tool calling
- Embeddings
- Vector similarity search
- Retrieval-Augmented Generation


## 9. Planned Integrations

The Angular application will send chat messages to the ASP.NET Core backend.

The backend will:

1. Receive and validate the customer question.
2. Send the question and available tool definitions to the AI model.
3. Execute controlled application tools when requested.
4. Retrieve relevant product or document data.
5. Send grounded context back to the model.
6. Return the final answer and source metadata to the frontend.

## 10. PoC Scope

The initial PoC will include:

- Product data import
- SQL product search
- Product availability and price questions
- AI tool calling
- Document-based knowledge retrieval
- Mixed product and knowledge questions
- Simple Angular chat interface
- Automated backend tests
- Basic documentation

## 11. Out of Scope

The following items are not required for the initial PoC:

- User registration
- Customer authentication
- Online payment
- Real order placement
- Real inventory integration
- Production deployment
- Image-based plant diagnosis
- Voice input
- Multilingual support
- Advanced administration interface
- Integration with a real garden center

## 12. Success Criteria

The PoC will be considered successful when it can demonstrate:

- Accurate product search from SQL data
- Accurate answers from approved knowledge documents
- At least one mixed structured and unstructured question
- Controlled AI tool usage
- Transparent handling of missing information
- A working chat demonstration
- Source code stored in a Git repository
- A final demo shorter than five minutes
