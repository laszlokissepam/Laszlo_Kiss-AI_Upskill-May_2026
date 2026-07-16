using System.Text.Json;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Dial;
using GardenBuddy.Application.Products;
using Microsoft.AspNetCore.Mvc;

namespace GardenBuddy.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
	private const string SearchProductsTool = "SearchProducts";
	private const string SearchKnowledgeBaseTool = "SearchKnowledgeBase";

	private readonly IDialApiService _dialApiService;
	private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly IProductService _productService;

	public ChatController(IDialApiService dialApiService, IKnowledgeBaseService knowledgeBaseService, IProductService productService)
	{
		_dialApiService = dialApiService;
		_knowledgeBaseService = knowledgeBaseService;
		_productService = productService;
	}

	/// <summary>
	/// Executes a chat turn with controlled backend tool-calling.
	/// </summary>
	/// <remarks>
	/// Example request:
	/// {
	///   "deploymentName": "gpt-4-turbo-deployment",
	///   "message": "Which beginner-friendly sunny balcony plants are in stock, and how should I care for them?",
	///   "temperature": 0.2,
	///   "maxTokens": 300
	/// }
	/// 
	/// Example response:
	/// {
	///   "answer": "Lavender and geranium are in stock and suitable for sunny balconies. Water deeply but infrequently.",
	///   "sources": [
	///     { "kind": "structured", "source": "Products", "itemId": "1", "score": 1.0 },
	///     { "kind": "unstructured", "source": "plant-care.md", "itemId": "plant-care.md#chunk-1", "score": 0.89 }
	///   ]
	/// }
	/// </remarks>
	[HttpPost]
	[Consumes("application/json")]
	[Produces("application/json")]
	[ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ChatErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ChatErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<IActionResult> PostAsync([FromBody] ChatRequest request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.DeploymentName))
		{
			return BadRequest(new ChatErrorResponse("DeploymentName is required."));
		}

		if (string.IsNullOrWhiteSpace(request.Message))
		{
			return BadRequest(new ChatErrorResponse("Message is required."));
		}

		var conversation = new List<DialChatMessage>
		{
			new("user", request.Message)
		};

		var tools = BuildChatTools();
		var collectedSources = new List<ChatSource>();

		try
		{
			var firstResponse = await _dialApiService.SendChatCompletionRequestAsync(
				request.DeploymentName,
				conversation,
				request.Temperature,
				request.MaxTokens,
				tools,
				"auto",
				false,
				cancellationToken);

			var firstAssistant = firstResponse.Choices.FirstOrDefault()?.Message;
			if (firstAssistant is null)
			{
				return StatusCode(StatusCodes.Status500InternalServerError, new ChatErrorResponse("Assistant returned no choices."));
			}

			if (firstAssistant.ToolCalls is null || firstAssistant.ToolCalls.Count == 0)
			{
				var forcedToolResponse = await _dialApiService.SendChatCompletionRequestAsync(
					request.DeploymentName,
					conversation,
					request.Temperature,
					request.MaxTokens,
					tools,
					"required",
					false,
					cancellationToken);

				firstAssistant = forcedToolResponse.Choices.FirstOrDefault()?.Message;
				if (firstAssistant is null)
				{
					return StatusCode(StatusCodes.Status500InternalServerError, new ChatErrorResponse("Assistant returned no choices after tool enforcement."));
				}

				if (firstAssistant.ToolCalls is null || firstAssistant.ToolCalls.Count == 0)
				{
					return Ok(new ChatResponse(
						"I could not retrieve reliable structured or knowledge-base data for this request.",
						Array.Empty<ChatSource>()));
				}
			}

			conversation.Add(new DialChatMessage("assistant", firstAssistant.Content, firstAssistant.ToolCalls));

			foreach (var toolCall in firstAssistant.ToolCalls)
			{
				var toolResult = await ExecuteToolCallAsync(toolCall, request.Message, cancellationToken);
				conversation.Add(new DialChatMessage("tool", toolResult.SerializedContent, ToolCallId: toolCall.Id));
				collectedSources.AddRange(toolResult.Sources);
			}

			if (collectedSources.Count == 0)
			{
				return Ok(new ChatResponse(
					"I could not retrieve reliable structured or knowledge-base data for this request.",
					Array.Empty<ChatSource>()));
			}

			var finalResponse = await _dialApiService.SendChatCompletionRequestAsync(
				request.DeploymentName,
				conversation,
				request.Temperature,
				request.MaxTokens,
				null,
				null,
				null,
				cancellationToken);

			var finalAssistant = finalResponse.Choices.FirstOrDefault()?.Message;
			if (finalAssistant is null)
			{
				return StatusCode(StatusCodes.Status500InternalServerError, new ChatErrorResponse("Final assistant response is missing."));
			}

			var distinctSources = collectedSources
				.DistinctBy(source => $"{source.Kind}:{source.Source}:{source.ItemId}")
				.ToArray();

			return Ok(new ChatResponse(finalAssistant.Content ?? string.Empty, distinctSources));
		}
		catch (DialApiException ex)
		{
			return StatusCode((int)ex.StatusCode, new ChatErrorResponse(ex.Message));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new ChatErrorResponse(ex.Message));
		}
		catch (InvalidOperationException ex)
		{
			return StatusCode(StatusCodes.Status500InternalServerError, new ChatErrorResponse(ex.Message));
		}
	}

	/// <summary>
	/// Alias route for chat completion orchestration with mixed tool support.
	/// </summary>
	[HttpPost("/api/chat-completion")]
	[Consumes("application/json")]
	[Produces("application/json")]
	[ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ChatErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ChatErrorResponse), StatusCodes.Status500InternalServerError)]
	public Task<IActionResult> ChatCompletionAsync([FromBody] ChatRequest request, CancellationToken cancellationToken)
	{
		return PostAsync(request, cancellationToken);
	}

	private async Task<ToolExecutionResult> ExecuteToolCallAsync(
		DialToolCall toolCall,
		string originalMessage,
		CancellationToken cancellationToken)
	{
		if (!string.Equals(toolCall.Type, "function", StringComparison.OrdinalIgnoreCase))
		{
			var unsupportedPayload = JsonSerializer.Serialize(new { error = $"Unsupported tool '{toolCall.Function.Name}'." });
			return new ToolExecutionResult(unsupportedPayload, Array.Empty<ChatSource>());
		}

		if (string.Equals(toolCall.Function.Name, SearchKnowledgeBaseTool, StringComparison.Ordinal))
		{
			var (query, topK) = ParseKnowledgeArguments(toolCall.Function.Arguments, originalMessage);
			var results = await _knowledgeBaseService.SearchAsync(query, topK, cancellationToken);

			var sources = results
				.Select(result => new ChatSource("unstructured", result.Source, result.ChunkId, result.Score))
				.ToArray();

			var serializedContent = JsonSerializer.Serialize(new
			{
				query,
				topK,
				results = results.Select(result => new
				{
					result.Source,
					result.ChunkId,
					result.Content,
					result.Score
				})
			});

			return new ToolExecutionResult(serializedContent, sources);
		}

		if (string.Equals(toolCall.Function.Name, SearchProductsTool, StringComparison.Ordinal))
		{
			var criteria = ParseProductArguments(toolCall.Function.Arguments, originalMessage);
			var results = await _productService.SearchAsync(criteria, cancellationToken);

			var sources = results
				.Select(result => new ChatSource("structured", "Products", result.Id.ToString(), 1.0))
				.ToArray();

			var serializedContent = JsonSerializer.Serialize(new
			{
				criteria,
				results
			});

			return new ToolExecutionResult(serializedContent, sources);
		}

		var unsupported = JsonSerializer.Serialize(new { error = $"Unsupported tool '{toolCall.Function.Name}'." });
		return new ToolExecutionResult(unsupported, Array.Empty<ChatSource>());
	}

	private static (string query, int topK) ParseKnowledgeArguments(string arguments, string fallbackQuery)
	{
		if (string.IsNullOrWhiteSpace(arguments))
		{
			return (fallbackQuery, 3);
		}

		using var json = JsonDocument.Parse(arguments);
		var root = json.RootElement;
		var query = root.TryGetProperty("query", out var queryElement) && !string.IsNullOrWhiteSpace(queryElement.GetString())
			? queryElement.GetString()!
			: fallbackQuery;

		var topK = root.TryGetProperty("topK", out var topKElement) && topKElement.TryGetInt32(out var requestedTopK)
			? requestedTopK
			: 3;

		return (query, Math.Clamp(topK, 1, 10));
	}

	private static ProductSearchCriteria ParseProductArguments(string arguments, string fallbackQuery)
	{
		if (string.IsNullOrWhiteSpace(arguments))
		{
			return new ProductSearchCriteria(Name: fallbackQuery, TopK: 5);
		}

		using var json = JsonDocument.Parse(arguments);
		var root = json.RootElement;

		string? name = ReadString(root, "name") ?? ReadString(root, "query") ?? fallbackQuery;
		string? category = ReadString(root, "category");
		string? sunlightRequirement = ReadString(root, "sunlightRequirement");
		string? difficulty = ReadString(root, "difficulty");
		decimal? minPrice = ReadDecimal(root, "minPrice");
		decimal? maxPrice = ReadDecimal(root, "maxPrice");
		bool? inStockOnly = ReadBool(root, "inStockOnly");
		var topK = ReadInt(root, "topK") ?? 5;

		return new ProductSearchCriteria(
			Name: name,
			Category: category,
			SunlightRequirement: sunlightRequirement,
			Difficulty: difficulty,
			MinPrice: minPrice,
			MaxPrice: maxPrice,
			InStockOnly: inStockOnly,
			TopK: Math.Clamp(topK, 1, 10));
	}

	private static IReadOnlyCollection<DialToolDefinition> BuildChatTools()
	{
		return new[]
		{
			new DialToolDefinition(
				"function",
				new DialToolDefinitionFunction(
					SearchProductsTool,
					"Searches structured SQL product data by business filters.",
					new
					{
						type = "object",
						properties = new
						{
							name = new { type = "string", description = "Product name or free-text product query." },
							category = new { type = "string", description = "Category filter such as Plant, Herb, Soil." },
							sunlightRequirement = new { type = "string" },
							difficulty = new { type = "string" },
							minPrice = new { type = "number" },
							maxPrice = new { type = "number" },
							inStockOnly = new { type = "boolean" },
							topK = new { type = "integer" }
						}
					})),
			new DialToolDefinition(
				"function",
				new DialToolDefinitionFunction(
					SearchKnowledgeBaseTool,
					"Searches knowledge base markdown chunks by semantic similarity.",
					new
					{
						type = "object",
						properties = new
						{
							query = new { type = "string", description = "Knowledge query text." },
							topK = new { type = "integer", description = "Number of chunks to return (1-10)." }
						},
						required = new[] { "query" }
					}))
		};
	}

	private static string? ReadString(JsonElement root, string propertyName)
	{
		return root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
	}

	private static int? ReadInt(JsonElement root, string propertyName)
	{
		return root.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out var value)
			? value
			: null;
	}

	private static bool? ReadBool(JsonElement root, string propertyName)
	{
		return root.TryGetProperty(propertyName, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
			? element.GetBoolean()
			: null;
	}

	private static decimal? ReadDecimal(JsonElement root, string propertyName)
	{
		if (root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Number)
		{
			if (element.TryGetDecimal(out var decimalValue))
			{
				return decimalValue;
			}
		}

		return null;
	}

	private sealed record ToolExecutionResult(string SerializedContent, IReadOnlyCollection<ChatSource> Sources);
}

public sealed record ChatRequest(
	string DeploymentName,
	string Message,
	double Temperature = 0.2,
	int MaxTokens = 300);

public sealed record ChatResponse(string Answer, IReadOnlyCollection<ChatSource> Sources);

public sealed record ChatSource(string Kind, string Source, string ItemId, double Score);

public sealed record ChatErrorResponse(string Error);
