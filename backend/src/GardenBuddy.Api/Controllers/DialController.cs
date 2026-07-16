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

	[HttpPost("chat-completions")]
	public async Task<IActionResult> ChatCompletionsAsync(
		[FromBody] DialChatCompletionApiRequest request,
		CancellationToken cancellationToken)
	{
		return await ExecuteChatCompletionAsync(request, cancellationToken);
	}

	[HttpPost("chat-completion")]
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
				cancellationToken);

			return Ok(result);
		}
		catch (DialApiException ex)
		{
			return StatusCode((int)ex.StatusCode, new { error = ex.Message });
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new { error = ex.Message });
		}
		catch (InvalidOperationException ex)
		{
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
		}
	}
}

public sealed record DialChatCompletionApiRequest(
	string DeploymentName,
	IReadOnlyCollection<DialChatMessage> Messages,
	double Temperature = 0.5,
	int MaxTokens = 200,
	IReadOnlyCollection<DialToolDefinition>? Tools = null,
	string? ToolChoice = null);
