using System.Net;
using GardenBuddy.Api.Controllers;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Dial;
using GardenBuddy.Application.Knowledge;
using GardenBuddy.Application.Products;
using Microsoft.AspNetCore.Mvc;

namespace GardenBuddy.Tests;

public class ChatControllerTests
{
	[Fact]
	public async Task PostAsync_ExecutesKnowledgeTool_AndReturnsFinalAnswerWithSources()
	{
		var dial = new FakeDialApiService();
		dial.QueueResponse(new DialChatCompletionResponse(
			"1",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(
					0,
					new DialChatMessage(
						"assistant",
						null,
						new[]
						{
							new DialToolCall(
								"call_1",
								"function",
								new DialToolFunction("SearchKnowledgeBase", "{\"query\":\"home delivery\",\"topK\":2}"))
						}),
					"tool_calls")
			}));
		dial.QueueResponse(new DialChatCompletionResponse(
			"2",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "We offer local home delivery."), "stop")
			}));

		var knowledge = new FakeKnowledgeBaseService
		{
			Results = new[]
			{
				new KnowledgeSearchResult("store-policies.md", "store-policies.md#chunk-1", "Home delivery is available", 0.91)
			}
		};

		var products = new FakeProductService();
		var controller = new ChatController(dial, knowledge, products);
		var result = await controller.PostAsync(new ChatRequest("gpt-4-turbo-deployment", "Do you offer home delivery?"), CancellationToken.None);

		var ok = Assert.IsType<OkObjectResult>(result);
		var payload = Assert.IsType<ChatResponse>(ok.Value);
		Assert.Contains("home delivery", payload.Answer, StringComparison.OrdinalIgnoreCase);
		Assert.Single(payload.Sources);
		Assert.Equal("store-policies.md", payload.Sources.First().Source);
		Assert.Equal(3, dial.CallCount);
	}

	[Fact]
	public async Task PostAsync_ExecutesMixedTools_AndReturnsLabeledSources()
	{
		var dial = new FakeDialApiService();
		dial.QueueResponse(new DialChatCompletionResponse(
			"1",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(
					0,
					new DialChatMessage(
						"assistant",
						null,
						new[]
						{
							new DialToolCall(
								"call_products",
								"function",
								new DialToolFunction("SearchProducts", "{\"category\":\"Plant\",\"inStockOnly\":true,\"topK\":2}")),
							new DialToolCall(
								"call_knowledge",
								"function",
								new DialToolFunction("SearchKnowledgeBase", "{\"query\":\"lavender care\",\"topK\":2}"))
						}),
					"tool_calls")
			}));

		dial.QueueResponse(new DialChatCompletionResponse(
			"2",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "Lavender is in stock and should be watered infrequently."), "stop")
			}));

		var knowledge = new FakeKnowledgeBaseService
		{
			Results = new[]
			{
				new KnowledgeSearchResult("plant-care.md", "plant-care.md#chunk-1", "Water deeply but infrequently.", 0.86)
			}
		};

		var products = new FakeProductService
		{
			Results = new[]
			{
				new ProductSearchResult(1, "Lavender", "Plant", "Fragrant perennial", 12.99m, 18, "Full Sun", "Low", "Beginner", false, true, "Non-toxic")
			}
		};

		var controller = new ChatController(dial, knowledge, products);
		var result = await controller.PostAsync(
			new ChatRequest("gpt-4-turbo-deployment", "Which beginner-friendly sunny balcony plants are in stock, and how should I care for them?"),
			CancellationToken.None);

		var ok = Assert.IsType<OkObjectResult>(result);
		var payload = Assert.IsType<ChatResponse>(ok.Value);
		Assert.Equal(2, payload.Sources.Count);
		Assert.Contains(payload.Sources, source => source.Kind == "structured" && source.Source == "Products");
		Assert.Contains(payload.Sources, source => source.Kind == "unstructured" && source.Source == "plant-care.md");
		Assert.Equal(2, dial.CallCount);
	}

	[Fact]
	public async Task PostAsync_ReturnsBadRequest_WhenMessageMissing()
	{
		var controller = new ChatController(new FakeDialApiService(), new FakeKnowledgeBaseService(), new FakeProductService());
		var result = await controller.PostAsync(new ChatRequest("dep", ""), CancellationToken.None);

		Assert.IsType<BadRequestObjectResult>(result);
	}

	[Fact]
	public async Task PostAsync_EnforcesToolCall_WhenInitialAssistantReplyHasNoToolCalls()
	{
		var dial = new FakeDialApiService();
		dial.QueueResponse(new DialChatCompletionResponse(
			"1",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "General guidance without sources."), "stop")
			}));

		dial.QueueResponse(new DialChatCompletionResponse(
			"2",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(
					0,
					new DialChatMessage(
						"assistant",
						null,
						new[]
						{
							new DialToolCall(
								"call_products",
								"function",
								new DialToolFunction("SearchProducts", "{\"name\":\"lavender\",\"inStockOnly\":true,\"topK\":1}"))
						}),
					"tool_calls")
			}));

		dial.QueueResponse(new DialChatCompletionResponse(
			"3",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "Lavender is in stock."), "stop")
			}));

		var knowledge = new FakeKnowledgeBaseService();
		var products = new FakeProductService
		{
			Results = new[]
			{
				new ProductSearchResult(1, "Lavender", "Plant", "Fragrant perennial", 12.99m, 18, "Full Sun", "Low", "Beginner", false, true, "Non-toxic")
			}
		};

		var controller = new ChatController(dial, knowledge, products);
		var result = await controller.PostAsync(
			new ChatRequest("gpt-4-turbo-deployment", "Mely kezdőbarát, napos erkélyre való növények vannak raktáron?"),
			CancellationToken.None);

		var ok = Assert.IsType<OkObjectResult>(result);
		var payload = Assert.IsType<ChatResponse>(ok.Value);
		Assert.Single(payload.Sources);
		Assert.Contains(payload.Sources, source => source.Kind == "structured");
		Assert.Equal(3, dial.CallCount);
	}

	[Fact]
	public async Task PostAsync_AddsStructuredFallback_WhenOnlyUnstructuredSourcesWereCollected()
	{
		var dial = new FakeDialApiService();
		dial.QueueResponse(new DialChatCompletionResponse(
			"1",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(
					0,
					new DialChatMessage(
						"assistant",
						null,
						new[]
						{
							new DialToolCall(
								"call_knowledge",
								"function",
								new DialToolFunction("SearchKnowledgeBase", "{\"query\":\"lavender\",\"topK\":2}"))
						}),
					"tool_calls")
			}));

		dial.QueueResponse(new DialChatCompletionResponse(
			"2",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "I do not have exact catalog pricing right now."), "stop")
			}));

		dial.QueueResponse(new DialChatCompletionResponse(
			"3",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(
					0,
					new DialChatMessage(
						"assistant",
						null,
						new[]
						{
							new DialToolCall(
								"forced_products",
								"function",
								new DialToolFunction("SearchProducts", "{\"name\":\"lavender\",\"topK\":1}"))
						}),
					"tool_calls")
			}
		));

		dial.QueueResponse(new DialChatCompletionResponse(
			"4",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "Lavender costs 12.99 based on catalog data."), "stop")
			}));

		var knowledge = new FakeKnowledgeBaseService
		{
			Results = new[]
			{
				new KnowledgeSearchResult("plant-care.md", "plant-care.md#chunk-1", "Lavender likes full sun.", 0.81)
			}
		};

		var products = new FakeProductService
		{
			Results = new[]
			{
				new ProductSearchResult(1, "Lavender", "Plant", "Fragrant perennial", 12.99m, 18, "Full Sun", "Low", "Beginner", false, true, "Non-toxic")
			}
		};

		var controller = new ChatController(dial, knowledge, products);
		var result = await controller.PostAsync(
			new ChatRequest("gpt-4-turbo-deployment", "How much a lavender cost?"),
			CancellationToken.None);

		var ok = Assert.IsType<OkObjectResult>(result);
		var payload = Assert.IsType<ChatResponse>(ok.Value);
		Assert.Contains(payload.Sources, source => source.Kind == "structured" && source.Source == "Products");
		Assert.Contains(payload.Sources, source => source.Kind == "unstructured" && source.Source == "plant-care.md");
		Assert.Contains("12.99", payload.Answer, StringComparison.Ordinal);
		Assert.Equal(4, dial.CallCount);
	}

	[Fact]
	public async Task PostAsync_BroadFallback_DoesNotUseWholeMessageAsProductName_WhenToolArgsAreEmptyObject()
	{
		var dial = new FakeDialApiService();
		dial.QueueResponse(new DialChatCompletionResponse(
			"1",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "I can answer directly."), "stop")
			}));

		dial.QueueResponse(new DialChatCompletionResponse(
			"2",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "Still no tool call."), "stop")
			}));

		dial.QueueResponse(new DialChatCompletionResponse(
			"3",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "Lavender price is available from catalog."), "stop")
			}));

		var knowledge = new FakeKnowledgeBaseService();
		var products = new FakeProductService
		{
			SearchHandler = criteria =>
				string.IsNullOrWhiteSpace(criteria.Name)
					? new[]
					{
						new ProductSearchResult(1, "Lavender", "Plant", "Fragrant perennial", 12.99m, 18, "Full Sun", "Low", "Beginner", false, true, "Non-toxic")
					}
					: Array.Empty<ProductSearchResult>()
		};

		var controller = new ChatController(dial, knowledge, products);
		var result = await controller.PostAsync(
			new ChatRequest("gpt-4-turbo-deployment", "How much one lavender cost and how fast can you ship it to me?"),
			CancellationToken.None);

		var ok = Assert.IsType<OkObjectResult>(result);
		var payload = Assert.IsType<ChatResponse>(ok.Value);
		Assert.Contains(payload.Sources, source => source.Kind == "structured" && source.Source == "Products");
		Assert.NotNull(products.LastCriteria);
		Assert.True(string.IsNullOrWhiteSpace(products.LastCriteria!.Name));
	}

	private sealed class FakeDialApiService : IDialApiService
	{
		private readonly Queue<DialChatCompletionResponse> _responses = new();

		public int CallCount { get; private set; }

		public void QueueResponse(DialChatCompletionResponse response)
		{
			_responses.Enqueue(response);
		}

		public Task<DialChatCompletionResponse> SendChatCompletionRequestAsync(
			string deploymentName,
			IReadOnlyCollection<DialChatMessage> messages,
			double temperature,
			int maxTokens,
			IReadOnlyCollection<DialToolDefinition>? tools = null,
			object? toolChoice = null,
			bool? parallelToolCalls = null,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			if (_responses.Count == 0)
			{
				throw new DialApiException(HttpStatusCode.InternalServerError, "No fake response configured");
			}

			return Task.FromResult(_responses.Dequeue());
		}
	}

	private sealed class FakeKnowledgeBaseService : IKnowledgeBaseService
	{
		public IReadOnlyCollection<KnowledgeSearchResult> Results { get; set; } = Array.Empty<KnowledgeSearchResult>();
		public Func<string, IReadOnlyCollection<KnowledgeSearchResult>>? SearchHandler { get; set; }
		public List<string> SearchedQueries { get; } = new();

		public Task<KnowledgeIngestionResult> IngestMarkdownDocumentsAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new KnowledgeIngestionResult(0, 0, 0, 0));
		}

		public Task<IReadOnlyCollection<KnowledgeSearchResult>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
		{
			SearchedQueries.Add(query);
			var result = SearchHandler?.Invoke(query) ?? Results;
			return Task.FromResult(result);
		}
	}

	private sealed class FakeProductService : IProductService
	{
		public IReadOnlyCollection<ProductSearchResult> Results { get; set; } = Array.Empty<ProductSearchResult>();
		public Func<ProductSearchCriteria, IReadOnlyCollection<ProductSearchResult>>? SearchHandler { get; set; }
		public ProductSearchCriteria? LastCriteria { get; private set; }

		public Task<IReadOnlyCollection<ProductSearchResult>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken = default)
		{
			LastCriteria = criteria;
			var result = SearchHandler?.Invoke(criteria) ?? Results;
			return Task.FromResult(result);
		}
	}
}
