using System.Text.Json;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Dial;
using Microsoft.AspNetCore.Mvc;

namespace GardenBuddy.Api.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
	private const string SearchKnowledgeBaseTool = "SearchKnowledgeBase";

	private readonly IDialApiService _dialApiService;
	private readonly IKnowledgeBaseService _knowledgeBaseService;

	public ChatController(IDialApiService dialApiService, IKnowledgeBaseService knowledgeBaseService)
	{
		_dialApiService = dialApiService;
		_knowledgeBaseService = knowledgeBaseService;
	}

	/// <summary>
	/// Executes a chat turn with controlled backend tool-calling.
	/// </summary>
	/// <remarks>
	/// Example request:
	/// {
	///   "deploymentName": "gpt-4-turbo-deployment",
	///   "message": "Do you offer home delivery?",
	///   "temperature": 0.2,
	///   "maxTokens": 300
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

		var tools = BuildKnowledgeTools();
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

			conversation.Add(new DialChatMessage("assistant", firstAssistant.Content, firstAssistant.ToolCalls));
			if (firstAssistant.ToolCalls is null || firstAssistant.ToolCalls.Count == 0)
			{
				return Ok(new ChatResponse(firstAssistant.Content ?? string.Empty, collectedSources));
			}

			foreach (var toolCall in firstAssistant.ToolCalls)
			{
				var toolResult = await ExecuteToolCallAsync(toolCall, request.Message, cancellationToken);
				conversation.Add(new DialChatMessage("tool", toolResult.SerializedContent, ToolCallId: toolCall.Id));
				collectedSources.AddRange(toolResult.Sources);
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
				.DistinctBy(source => source.ChunkId)
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

	private async Task<ToolExecutionResult> ExecuteToolCallAsync(
		DialToolCall toolCall,
		string originalMessage,
		CancellationToken cancellationToken)
	{
		if (!string.Equals(toolCall.Type, "function", StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(toolCall.Function.Name, SearchKnowledgeBaseTool, StringComparison.Ordinal))
		{
			var unsupportedPayload = JsonSerializer.Serialize(new { error = $"Unsupported tool '{toolCall.Function.Name}'." });
			return new ToolExecutionResult(unsupportedPayload, Array.Empty<ChatSource>());
		}

		var (query, topK) = ParseKnowledgeArguments(toolCall.Function.Arguments, originalMessage);
		var results = await _knowledgeBaseService.SearchAsync(query, topK, cancellationToken);

		var sources = results
			.Select(result => new ChatSource("knowledge", result.Source, result.ChunkId, result.Score))
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

	private static IReadOnlyCollection<DialToolDefinition> BuildKnowledgeTools()
	{
		return new[]
		{
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

	private sealed record ToolExecutionResult(string SerializedContent, IReadOnlyCollection<ChatSource> Sources);
}

public sealed record ChatRequest(
	string DeploymentName,
	string Message,
	double Temperature = 0.2,
	int MaxTokens = 300);

public sealed record ChatResponse(string Answer, IReadOnlyCollection<ChatSource> Sources);

public sealed record ChatSource(string Type, string Source, string ChunkId, double Score);

public sealed record ChatErrorResponse(string Error);
