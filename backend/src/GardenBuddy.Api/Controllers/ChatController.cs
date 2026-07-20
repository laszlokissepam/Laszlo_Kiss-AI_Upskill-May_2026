using System.Text.Json;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Dial;
using GardenBuddy.Application.Knowledge;
using GardenBuddy.Application.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;

namespace GardenBuddy.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
	private const string SearchProductsTool = "SearchProducts";
	private const string SearchKnowledgeBaseTool = "SearchKnowledgeBase";
	private const double MinimumUnstructuredSourceScore = 0.15;
	private const int MaxToolRounds = 4;
	private static readonly HashSet<string> ProductInferenceStopwords = new(StringComparer.OrdinalIgnoreCase)
	{
		"a", "an", "and", "are", "can", "cost", "do", "does", "for", "from", "how", "i", "in", "is", "it", "much", "need", "of", "on", "one", "or", "please", "price", "shipping", "the", "to", "what", "which", "with", "you", "your",
		"egy", "es", "hogy", "is", "kell", "kerem", "mennyi", "mi", "mit", "most", "nekem", "szallit", "szallitas", "van", "vagy"
	};

	private readonly IDialApiService _dialApiService;
	private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly IProductService _productService;
    private readonly ILogger<ChatController> _logger;

	public ChatController(IDialApiService dialApiService, IKnowledgeBaseService knowledgeBaseService, IProductService productService, ILogger<ChatController>? logger = null)
	{
		_dialApiService = dialApiService;
		_knowledgeBaseService = knowledgeBaseService;
		_productService = productService;
		_logger = logger ?? NullLogger<ChatController>.Instance;
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
			var response = await _dialApiService.SendChatCompletionRequestAsync(
				request.DeploymentName,
				conversation,
				request.Temperature,
				request.MaxTokens,
				tools,
				"auto",
				false,
				cancellationToken);

			var assistant = response.Choices.FirstOrDefault()?.Message;
			if (assistant is null)
			{
				return StatusCode(StatusCodes.Status500InternalServerError, new ChatErrorResponse("Assistant returned no choices."));
			}

			if (assistant.ToolCalls is null || assistant.ToolCalls.Count == 0)
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

				assistant = forcedToolResponse.Choices.FirstOrDefault()?.Message;
				if (assistant is null)
				{
					return StatusCode(StatusCodes.Status500InternalServerError, new ChatErrorResponse("Assistant returned no choices after tool enforcement."));
				}

				if (assistant.ToolCalls is null || assistant.ToolCalls.Count == 0)
				{
					var fallback = await ExecuteBroadFallbackAsync(
						request.DeploymentName,
						request.Message,
						request.Temperature,
						request.MaxTokens,
						cancellationToken);

					if (fallback is not null)
					{
						return Ok(fallback);
					}

					return Ok(new ChatResponse(
						"I could not retrieve reliable structured or knowledge-base data for this request.",
						Array.Empty<ChatSource>()));
				}
			}

			for (var round = 0; round < MaxToolRounds; round++)
			{
				conversation.Add(new DialChatMessage("assistant", assistant.Content, assistant.ToolCalls));

				if (assistant.ToolCalls is null || assistant.ToolCalls.Count == 0)
				{
					break;
				}

				foreach (var toolCall in assistant.ToolCalls)
				{
					var toolResult = await ExecuteToolCallAsync(toolCall, request.Message, cancellationToken);
					conversation.Add(new DialChatMessage("tool", toolResult.SerializedContent, ToolCallId: toolCall.Id));
					collectedSources.AddRange(toolResult.Sources);
				}

				response = await _dialApiService.SendChatCompletionRequestAsync(
					request.DeploymentName,
					conversation,
					request.Temperature,
					request.MaxTokens,
					tools,
					"auto",
					false,
					cancellationToken);

				assistant = response.Choices.FirstOrDefault()?.Message;
				if (assistant is null)
				{
					return StatusCode(StatusCodes.Status500InternalServerError, new ChatErrorResponse("Final assistant response is missing."));
				}
			}

			if (collectedSources.Count == 0)
			{
				var fallback = await ExecuteBroadFallbackAsync(
					request.DeploymentName,
					request.Message,
					request.Temperature,
					request.MaxTokens,
					cancellationToken);

				if (fallback is not null)
				{
					return Ok(fallback);
				}

				return Ok(new ChatResponse(
					"I could not retrieve reliable structured or knowledge-base data for this request.",
					Array.Empty<ChatSource>()));
			}

			if (!collectedSources.Any(source => string.Equals(source.Kind, "structured", StringComparison.Ordinal)))
			{
				var structuredFallback = await ExecuteStructuredFallbackAsync(
					request.DeploymentName,
					request.Message,
					request.Temperature,
					request.MaxTokens,
					conversation,
					assistant.Content ?? string.Empty,
					collectedSources,
					cancellationToken);

				if (structuredFallback is not null)
				{
					return Ok(structuredFallback);
				}
			}

			var distinctSources = collectedSources
				.DistinctBy(source => $"{source.Kind}:{source.Source}:{source.ItemId}")
				.ToArray();

			return Ok(new ChatResponse(assistant.Content ?? string.Empty, distinctSources));
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
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex, "DIAL API is unreachable while processing a chat request.");
			return StatusCode(
				StatusCodes.Status503ServiceUnavailable,
				new ChatErrorResponse("The AI service is temporarily unavailable. Please try again later."));
		}
	}

	private async Task<ChatResponse?> ExecuteBroadFallbackAsync(
		string deploymentName,
		string originalMessage,
		double temperature,
		int maxTokens,
		CancellationToken cancellationToken)
	{
		var fallbackSources = new List<ChatSource>();
		var fallbackConversation = new List<DialChatMessage>
		{
			new("user", originalMessage)
		};

		var productToolCallId = "fallback_products";
		var knowledgeToolCallId = "fallback_knowledge";

		var productResult = await ExecuteToolCallAsync(
			new DialToolCall(
				productToolCallId,
				"function",
				new DialToolFunction(SearchProductsTool, "{}")),
			originalMessage,
			cancellationToken);

		fallbackConversation.Add(new DialChatMessage(
			"assistant",
			null,
			new[]
			{
				new DialToolCall(productToolCallId, "function", new DialToolFunction(SearchProductsTool, "{}"))
			}));
		fallbackConversation.Add(new DialChatMessage("tool", productResult.SerializedContent, ToolCallId: productToolCallId));
		fallbackSources.AddRange(productResult.Sources);

		var knowledgeResult = await ExecuteToolCallAsync(
			new DialToolCall(
				knowledgeToolCallId,
				"function",
				new DialToolFunction(SearchKnowledgeBaseTool, "{}")),
			originalMessage,
			cancellationToken);

		fallbackConversation.Add(new DialChatMessage(
			"assistant",
			null,
			new[]
			{
				new DialToolCall(knowledgeToolCallId, "function", new DialToolFunction(SearchKnowledgeBaseTool, "{}"))
			}));
		fallbackConversation.Add(new DialChatMessage("tool", knowledgeResult.SerializedContent, ToolCallId: knowledgeToolCallId));
		fallbackSources.AddRange(knowledgeResult.Sources);

		if (fallbackSources.Count == 0)
		{
			return null;
		}

		var distinctSources = fallbackSources
			.DistinctBy(source => $"{source.Kind}:{source.Source}:{source.ItemId}")
			.ToArray();

		var finalResponse = await _dialApiService.SendChatCompletionRequestAsync(
			deploymentName,
			fallbackConversation,
			temperature,
			maxTokens,
			null,
			null,
			null,
			cancellationToken);

		var finalAssistant = finalResponse.Choices.FirstOrDefault()?.Message;
		if (finalAssistant is null)
		{
			return new ChatResponse(
				"I found relevant product and policy sources, but could not generate a final answer text.",
				distinctSources);
		}

		return new ChatResponse(finalAssistant.Content ?? string.Empty, distinctSources);
	}

	private async Task<ChatResponse?> ExecuteStructuredFallbackAsync(
		string deploymentName,
		string originalMessage,
		double temperature,
		int maxTokens,
		IReadOnlyCollection<DialChatMessage> conversation,
		string currentAssistantAnswer,
		IReadOnlyCollection<ChatSource> existingSources,
		CancellationToken cancellationToken)
	{
		const string structuredFallbackToolCallId = "fallback_products";
		var productToolCall = await GetForcedProductToolCallAsync(
			deploymentName,
			conversation,
			temperature,
			maxTokens,
			cancellationToken)
			?? new DialToolCall(
				structuredFallbackToolCallId,
				"function",
				new DialToolFunction(SearchProductsTool, "{}"));

		var productToolCallId = string.IsNullOrWhiteSpace(productToolCall.Id)
			? structuredFallbackToolCallId
			: productToolCall.Id;

		var productResult = await ExecuteToolCallAsync(
			new DialToolCall(
				productToolCallId,
				"function",
				new DialToolFunction(SearchProductsTool, productToolCall.Function.Arguments)),
			originalMessage,
			cancellationToken);

		if (productResult.Sources.Count == 0)
		{
			return null;
		}

		var fallbackConversation = new List<DialChatMessage>(conversation)
		{
			new(
				"assistant",
				null,
				new[]
				{
					new DialToolCall(productToolCallId, "function", new DialToolFunction(SearchProductsTool, productToolCall.Function.Arguments))
				}),
			new("tool", productResult.SerializedContent, ToolCallId: productToolCallId)
		};

		var finalResponse = await _dialApiService.SendChatCompletionRequestAsync(
			deploymentName,
			fallbackConversation,
			temperature,
			maxTokens,
			null,
			null,
			null,
			cancellationToken);

		var finalAssistant = finalResponse.Choices.FirstOrDefault()?.Message;
		var answer = finalAssistant?.Content ?? currentAssistantAnswer;
		var mergedSources = existingSources
			.Concat(productResult.Sources)
			.DistinctBy(source => $"{source.Kind}:{source.Source}:{source.ItemId}")
			.ToArray();

		return new ChatResponse(answer, mergedSources);
	}

	private async Task<DialToolCall?> GetForcedProductToolCallAsync(
		string deploymentName,
		IReadOnlyCollection<DialChatMessage> conversation,
		double temperature,
		int maxTokens,
		CancellationToken cancellationToken)
	{
		try
		{
			var response = await _dialApiService.SendChatCompletionRequestAsync(
				deploymentName,
				conversation,
				temperature,
				maxTokens,
				BuildChatTools(),
				new
				{
					type = "function",
					function = new
					{
						name = SearchProductsTool
					}
				},
				false,
				cancellationToken);

			var assistant = response.Choices.FirstOrDefault()?.Message;
			if (assistant?.ToolCalls is null || assistant.ToolCalls.Count == 0)
			{
				return null;
			}

			return assistant.ToolCalls.FirstOrDefault(toolCall =>
				string.Equals(toolCall.Function.Name, SearchProductsTool, StringComparison.Ordinal));
		}
		catch (DialApiException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
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
			var filteredResults = results
				.Where(result => result.Score >= MinimumUnstructuredSourceScore)
				.ToArray();

			var sources = filteredResults
				.Select(result => new ChatSource("unstructured", result.Source, result.ChunkId, result.Score))
				.ToArray();

			var serializedContent = JsonSerializer.Serialize(new
			{
				query,
				topK,
				results = filteredResults.Select(result => new
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
			ProductSearchCriteria criteria;
			IReadOnlyCollection<ProductSearchResult>? inferredResults = null;
			try
			{
				criteria = ParseProductArguments(toolCall.Function.Arguments, originalMessage);
			}
			catch (JsonException ex)
			{
				_logger.LogWarning(ex, "Invalid JSON arguments for {ToolName}: {Arguments}", SearchProductsTool, toolCall.Function.Arguments);
				var invalidPayload = JsonSerializer.Serialize(new { error = "Invalid tool arguments for SearchProducts." });
				return new ToolExecutionResult(invalidPayload, Array.Empty<ChatSource>());
			}
			catch (ArgumentException ex)
			{
				_logger.LogWarning(ex, "Invalid arguments for {ToolName}: {Arguments}", SearchProductsTool, toolCall.Function.Arguments);
				var invalidPayload = JsonSerializer.Serialize(new { error = ex.Message });
				return new ToolExecutionResult(invalidPayload, Array.Empty<ChatSource>());
			}

			if (!HasAnyProductFilter(
				criteria.Name,
				criteria.Category,
				criteria.SunlightRequirement,
				criteria.Difficulty,
				criteria.MinPrice,
				criteria.MaxPrice,
				criteria.InStockOnly))
			{
				var inferred = await TryInferProductCriteriaFromMessageAsync(originalMessage, cancellationToken);
				if (inferred is null)
				{
					_logger.LogWarning("SearchProducts was called without filters and no product name could be inferred. Arguments: {Arguments}", toolCall.Function.Arguments);
					var invalidPayload = JsonSerializer.Serialize(new { error = "SearchProducts requires at least one filter argument." });
					return new ToolExecutionResult(invalidPayload, Array.Empty<ChatSource>());
				}

				criteria = inferred.Criteria;
				inferredResults = inferred.Results;
			}

			var results = inferredResults ?? await _productService.SearchAsync(criteria, cancellationToken);
			if (results.Count == 0 && LooksLikeBroadNaturalLanguageName(criteria.Name))
			{
				var inferredFromMessage = await TryInferProductCriteriaFromMessageAsync(originalMessage, cancellationToken);
				if (inferredFromMessage is not null)
				{
					criteria = inferredFromMessage.Criteria;
					results = inferredFromMessage.Results;
				}
			}

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
			return new ProductSearchCriteria(Name: null, TopK: 10);
		}

		using var json = JsonDocument.Parse(arguments);
		var root = json.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new ArgumentException("Product tool arguments must be a JSON object.", nameof(arguments));
		}

		string? name = ReadString(root, "name") ?? ReadString(root, "query");
		string? category = ReadString(root, "category");
		string? sunlightRequirement = ReadString(root, "sunlightRequirement");
		string? difficulty = ReadString(root, "difficulty");
		decimal? minPrice = ReadDecimal(root, "minPrice");
		decimal? maxPrice = ReadDecimal(root, "maxPrice");
		if (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice)
		{
			throw new ArgumentException("minPrice cannot be greater than maxPrice.", nameof(arguments));
		}

		bool? inStockOnly = ReadBool(root, "inStockOnly");
		var topK = ReadInt(root, "topK") ?? 5;

		if (!HasAnyProductFilter(name, category, sunlightRequirement, difficulty, minPrice, maxPrice, inStockOnly))
		{
			return new ProductSearchCriteria(Name: null, TopK: Math.Clamp(topK, 1, 10));
		}

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

	private static bool HasAnyProductFilter(
		string? name,
		string? category,
		string? sunlightRequirement,
		string? difficulty,
		decimal? minPrice,
		decimal? maxPrice,
		bool? inStockOnly)
	{
		return !string.IsNullOrWhiteSpace(name)
			|| !string.IsNullOrWhiteSpace(category)
			|| !string.IsNullOrWhiteSpace(sunlightRequirement)
			|| !string.IsNullOrWhiteSpace(difficulty)
			|| minPrice.HasValue
			|| maxPrice.HasValue
			|| inStockOnly.HasValue;
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

	private async Task<InferredProductSearch?> TryInferProductCriteriaFromMessageAsync(string originalMessage, CancellationToken cancellationToken)
	{
		var tokenMatches = Regex.Matches(originalMessage, "[\\p{L}\\p{N}-]+")
			.Select(match => match.Value.Trim())
			.Where(token => token.Length >= 3)
			.Where(token => !ProductInferenceStopwords.Contains(token))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(token => token.Length)
			.Take(8)
			.ToArray();

		foreach (var token in tokenMatches)
		{
			var criteria = new ProductSearchCriteria(Name: token, TopK: 5);
			var results = await _productService.SearchAsync(criteria, cancellationToken);
			if (results.Count > 0)
			{
				_logger.LogInformation("Inferred product filter '{Token}' for SearchProducts fallback.", token);
				return new InferredProductSearch(criteria, results);
			}
		}

		return null;
	}

	private static bool LooksLikeBroadNaturalLanguageName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		var tokenCount = Regex.Matches(name, "[\\p{L}\\p{N}-]+").Count;
		return tokenCount >= 4 || name.Contains('?', StringComparison.Ordinal);
	}

	private sealed record ToolExecutionResult(string SerializedContent, IReadOnlyCollection<ChatSource> Sources);
	private sealed record InferredProductSearch(ProductSearchCriteria Criteria, IReadOnlyCollection<ProductSearchResult> Results);
}

public sealed record ChatRequest(
	string DeploymentName,
	string Message,
	double Temperature = 0.2,
	int MaxTokens = 300);

public sealed record ChatResponse(string Answer, IReadOnlyCollection<ChatSource> Sources);

public sealed record ChatSource(string Kind, string Source, string ItemId, double Score);

public sealed record ChatErrorResponse(string Error);
