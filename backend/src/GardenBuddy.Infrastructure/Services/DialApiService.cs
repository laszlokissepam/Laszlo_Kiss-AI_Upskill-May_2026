using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Configuration;
using GardenBuddy.Application.Dial;
using Microsoft.Extensions.Options;

namespace GardenBuddy.Infrastructure.Services;

public sealed class DialApiService : IDialApiService
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _httpClient;
	private readonly DialApiOptions _options;

	public DialApiService(HttpClient httpClient, IOptions<DialApiOptions> options)
	{
		_httpClient = httpClient;
		_options = options.Value;
	}

	public async Task<DialChatCompletionResponse> SendChatCompletionRequestAsync(
		string deploymentName,
		IReadOnlyCollection<DialChatMessage> messages,
		double temperature,
		int maxTokens,
		IReadOnlyCollection<DialToolDefinition>? tools = null,
		string? toolChoice = null,
		CancellationToken cancellationToken = default)
	{
		ValidateInputs(deploymentName, messages, maxTokens, tools);

		var requestBody = new DialChatCompletionRequest(
			Model: _options.DefaultModel,
			Messages: messages,
			Temperature: temperature,
			TopP: 1,
			MaxTokens: maxTokens,
			Tools: tools,
			ToolChoice: toolChoice);

		var endpoint = $"openai/deployments/{Uri.EscapeDataString(deploymentName)}/chat/completions?api-version={Uri.EscapeDataString(_options.ApiVersion)}";
		using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = JsonContent.Create(requestBody, options: JsonOptions)
		};

		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ResolveApiKey());
		request.Headers.Remove("X-CACHE-POLICY");
		request.Headers.Add("X-CACHE-POLICY", _options.CachePolicy);

		using var response = await _httpClient.SendAsync(request, cancellationToken);
		var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
		{
			throw CreateException(response.StatusCode, responseBody);
		}

		var parsedResponse = JsonSerializer.Deserialize<DialChatCompletionResponse>(responseBody, JsonOptions);
		if (parsedResponse is null || string.IsNullOrWhiteSpace(parsedResponse.Id) || parsedResponse.Choices is null)
		{
			throw new DialApiException(HttpStatusCode.InternalServerError, "DIAL API returned an empty or invalid response payload.", responseBody);
		}

		return parsedResponse;
	}

	private string ResolveApiKey()
	{
		var environmentApiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
		var apiKey = string.IsNullOrWhiteSpace(environmentApiKey) ? _options.ApiKey : environmentApiKey;

		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new InvalidOperationException($"DIAL API key is missing. Set the '{_options.ApiKeyEnvironmentVariable}' environment variable or provide Dial:ApiKey in configuration.");
		}

		return apiKey;
	}

	private static void ValidateInputs(
		string deploymentName,
		IReadOnlyCollection<DialChatMessage> messages,
		int maxTokens,
		IReadOnlyCollection<DialToolDefinition>? tools)
	{
		if (string.IsNullOrWhiteSpace(deploymentName))
		{
			throw new ArgumentException("Deployment name is required.", nameof(deploymentName));
		}

		if (messages.Count == 0)
		{
			throw new ArgumentException("At least one message is required.", nameof(messages));
		}

		if (maxTokens <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxTokens), "Max tokens must be greater than zero.");
		}

		if (tools is null)
		{
			return;
		}

		foreach (var tool in tools)
		{
			if (!string.Equals(tool.Type, "function", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("Only function tools are supported.", nameof(tools));
			}

			if (string.IsNullOrWhiteSpace(tool.Function.Name))
			{
				throw new ArgumentException("Tool function name is required.", nameof(tools));
			}
		}
	}

	private static DialApiException CreateException(HttpStatusCode statusCode, string responseBody)
	{
		var message = statusCode switch
		{
			HttpStatusCode.BadRequest => "DIAL API request was invalid (400 Bad Request).",
			HttpStatusCode.Unauthorized => "DIAL API authentication failed (401 Unauthorized).",
			HttpStatusCode.InternalServerError => "DIAL API encountered an internal error (500 Internal Server Error).",
			_ => $"DIAL API request failed with status code {(int)statusCode}."
		};

		return new DialApiException(statusCode, message, responseBody);
	}
}
