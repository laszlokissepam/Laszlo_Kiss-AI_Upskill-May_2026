using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Dial;
using Microsoft.AspNetCore.Mvc;

namespace GardenBuddy.Api.Controllers;

[ApiController]
[Route("api/dial")]
public sealed class DialController : ControllerBase
{
	private readonly IDialApiService _dialApiService;

	public DialController(IDialApiService dialApiService)
	{
		_dialApiService = dialApiService;
	}

	/// <summary>
	/// Sends a chat-completion request to the configured DIAL deployment.
	/// </summary>
	/// <remarks>
	/// Example tool-enabled request:
	/// {
	///   "deploymentName": "gpt-4-turbo-deployment",
	///   "messages": [
	///     { "role": "user", "content": "Find beginner plants for a sunny balcony." }
	///   ],
	///   "temperature": 0.4,
	///   "maxTokens": 200,
	///   "tools": [
	///     {
	///       "type": "function",
	///       "function": {
	///         "name": "SearchProducts",
	///         "description": "Search products by catalog filters",
	///         "parameters": {
	///           "type": "object",
	///           "properties": { "category": { "type": "string" } },
	///           "required": ["category"]
	///         }
	///       }
	///     }
	///   ],
	///   "toolChoice": "auto",
	///   "parallelToolCalls": true
	/// }
	/// </remarks>
	[HttpPost("chat-completions")]
	[Consumes("application/json")]
	[Produces("application/json")]
	[ProducesResponseType(typeof(DialChatCompletionResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(DialErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(DialErrorResponse), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(DialErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<IActionResult> ChatCompletionsAsync(
		[FromBody] DialChatCompletionApiRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteChatCompletionAsync(request, cancellationToken);
	}

	/// <summary>
	/// Backward-compatible alias for the chat-completions route.
	/// </summary>
	[HttpPost("chat-completion")]
	[Consumes("application/json")]
	[Produces("application/json")]
	[ProducesResponseType(typeof(DialChatCompletionResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(DialErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(DialErrorResponse), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(DialErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<IActionResult> ChatCompletionAsync(
		[FromBody] DialChatCompletionApiRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteChatCompletionAsync(request, cancellationToken);
	}

	private async Task<IActionResult> ExecuteChatCompletionAsync(
		DialChatCompletionApiRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var result = await _dialApiService.SendChatCompletionRequestAsync(
				request.DeploymentName,
				request.Messages,
				request.Temperature,
				request.MaxTokens,
				request.Tools,
				request.ToolChoice,
				request.ParallelToolCalls,
				cancellationToken);

			return Ok(result);
		}
		catch (DialApiException ex)
		{
			return StatusCode((int)ex.StatusCode, new DialErrorResponse(ex.Message));
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new DialErrorResponse(ex.Message));
		}
		catch (InvalidOperationException ex)
		{
			return StatusCode(StatusCodes.Status500InternalServerError, new DialErrorResponse(ex.Message));
		}
	}
}

/// <summary>
/// API payload for DIAL chat completion calls.
/// </summary>
public sealed record DialChatCompletionApiRequest(
	string DeploymentName,
	IReadOnlyCollection<DialChatMessage> Messages,
	double Temperature = 0.5,
	int MaxTokens = 200,
	IReadOnlyCollection<DialToolDefinition>? Tools = null,
	object? ToolChoice = null,
	bool? ParallelToolCalls = null);

public sealed record DialErrorResponse(string Error);
