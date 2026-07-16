using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Configuration;
using GardenBuddy.Application.Dial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GardenBuddy.Infrastructure.Services;

public sealed class DialApiService : IDialApiService
{
	private const int MaxTools = 128;
	private const int MaxLoggedProviderResponseLength = 1000;
	private static readonly StringComparer ToolNameComparer = StringComparer.Ordinal;
	private static readonly HashSet<string> SupportedToolChoiceLiterals = new(StringComparer.OrdinalIgnoreCase)
	{
		"auto",
		"none",
		"required"
	};

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _httpClient;
	private readonly DialApiOptions _options;
	private readonly ILogger<DialApiService> _logger;

	public DialApiService(HttpClient httpClient, IOptions<DialApiOptions> options, ILogger<DialApiService> logger)
	{
		_httpClient = httpClient;
		_options = options.Value;
		_logger = logger;
	}

	public async Task<DialChatCompletionResponse> SendChatCompletionRequestAsync(
		string deploymentName,
		IReadOnlyCollection<DialChatMessage> messages,
		double temperature,
		int maxTokens,
		IReadOnlyCollection<DialToolDefinition>? tools = null,
		object? toolChoice = null,
		bool? parallelToolCalls = null,
		CancellationToken cancellationToken = default)
	{
		ValidateInputs(deploymentName, messages, temperature, maxTokens, tools, toolChoice);

		var requestBody = new DialChatCompletionRequest(
			Model: _options.DefaultModel,
			Messages: messages,
			Temperature: temperature,
			TopP: 1,
			MaxTokens: maxTokens,
			Tools: tools,
			ToolChoice: toolChoice,
			ParallelToolCalls: parallelToolCalls);

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
		var correlationId = GetCorrelationId(response);

		if (!response.IsSuccessStatusCode)
		{
			_logger.LogWarning(
				"DIAL API call failed. StatusCode={StatusCode}. CorrelationId={CorrelationId}. ProviderResponse={ProviderResponse}",
				(int)response.StatusCode,
				correlationId ?? "n/a",
				ShortenForLog(responseBody));

			throw CreateException(response.StatusCode, responseBody);
		}

		var parsedResponse = JsonSerializer.Deserialize<DialChatCompletionResponse>(responseBody, JsonOptions);
		if (parsedResponse is null || string.IsNullOrWhiteSpace(parsedResponse.Id) || parsedResponse.Choices is null)
		{
			_logger.LogError(
				"DIAL API returned invalid payload. CorrelationId={CorrelationId}. ProviderResponse={ProviderResponse}",
				correlationId ?? "n/a",
				ShortenForLog(responseBody));

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
		double temperature,
		int maxTokens,
		IReadOnlyCollection<DialToolDefinition>? tools,
		object? toolChoice)
	{
		if (string.IsNullOrWhiteSpace(deploymentName))
		{
			throw new ArgumentException("Deployment name is required.", nameof(deploymentName));
		}

		if (messages.Count == 0)
		{
			throw new ArgumentException("At least one message is required.", nameof(messages));
		}

		if (temperature is < 0 or > 2)
		{
			throw new ArgumentOutOfRangeException(nameof(temperature), "Temperature must be between 0 and 2.");
		}

		if (maxTokens <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxTokens), "Max tokens must be greater than zero.");
		}

		if (tools is null)
		{
			ValidateToolChoice(toolChoice, toolNames: null);
			return;
		}

		if (tools.Count > MaxTools)
		{
			throw new ArgumentException($"A maximum of {MaxTools} tools is allowed.", nameof(tools));
		}

		var toolNames = new HashSet<string>(ToolNameComparer);
		var index = 0;

		foreach (var tool in tools)
		{
			index++;

			if (!string.Equals(tool.Type, "function", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException($"Tool at index {index - 1} must have type 'function'.", nameof(tools));
			}

			if (string.IsNullOrWhiteSpace(tool.Function.Name))
			{
				throw new ArgumentException($"Tool at index {index - 1} is missing function name.", nameof(tools));
			}

			if (!toolNames.Add(tool.Function.Name))
			{
				throw new ArgumentException($"Duplicate tool function name '{tool.Function.Name}' is not allowed.", nameof(tools));
			}

			if (string.IsNullOrWhiteSpace(tool.Function.Description))
			{
				throw new ArgumentException($"Tool '{tool.Function.Name}' is missing function description.", nameof(tools));
			}

			if (tool.Function.Parameters is null)
			{
				throw new ArgumentException($"Tool '{tool.Function.Name}' is missing JSON schema parameters.", nameof(tools));
			}

			var schema = JsonSerializer.SerializeToElement(tool.Function.Parameters, JsonOptions);
			ValidateToolParametersSchema(tool.Function.Name, schema, nameof(tools));
		}

		ValidateToolChoice(toolChoice, toolNames);
	}

	private static void ValidateToolChoice(object? toolChoice, ISet<string>? toolNames)
	{
		if (toolChoice is null)
		{
			return;
		}

		if (toolChoice is string literalChoice)
		{
			if (!SupportedToolChoiceLiterals.Contains(literalChoice))
			{
				throw new ArgumentException("Tool choice must be one of: auto, none, required, or a specific function choice object.", nameof(toolChoice));
			}

			return;
		}

		var toolChoiceElement = JsonSerializer.SerializeToElement(toolChoice, JsonOptions);
		if (toolChoiceElement.ValueKind != JsonValueKind.Object)
		{
			throw new ArgumentException("Tool choice object must be a JSON object.", nameof(toolChoice));
		}

		if (!toolChoiceElement.TryGetProperty("type", out var typeElement)
			|| !string.Equals(typeElement.GetString(), "function", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Tool choice object must have type='function'.", nameof(toolChoice));
		}

		if (!toolChoiceElement.TryGetProperty("function", out var functionElement)
			|| functionElement.ValueKind != JsonValueKind.Object)
		{
			throw new ArgumentException("Tool choice object must include a function object.", nameof(toolChoice));
		}

		if (!functionElement.TryGetProperty("name", out var nameElement)
			|| string.IsNullOrWhiteSpace(nameElement.GetString()))
		{
			throw new ArgumentException("Tool choice function name is required.", nameof(toolChoice));
		}

		if (toolNames is null || toolNames.Count == 0)
		{
			throw new ArgumentException("Specific tool choice requires at least one tool definition.", nameof(toolChoice));
		}

		var toolName = nameElement.GetString()!;
		if (!toolNames.Contains(toolName))
		{
			throw new ArgumentException($"Tool choice function '{toolName}' is not defined in tools.", nameof(toolChoice));
		}
	}

	private static void ValidateToolParametersSchema(string toolName, JsonElement schema, string paramName)
	{
		if (schema.ValueKind != JsonValueKind.Object)
		{
			throw new ArgumentException($"Tool '{toolName}' parameters must be a JSON object schema.", paramName);
		}

		if (!schema.TryGetProperty("type", out var typeElement)
			|| !string.Equals(typeElement.GetString(), "object", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException($"Tool '{toolName}' schema must define type='object'.", paramName);
		}

		if (!schema.TryGetProperty("properties", out var propertiesElement)
			|| propertiesElement.ValueKind != JsonValueKind.Object)
		{
			throw new ArgumentException($"Tool '{toolName}' schema must define an object 'properties' section.", paramName);
		}

		if (!schema.TryGetProperty("required", out var requiredElement))
		{
			return;
		}

		if (requiredElement.ValueKind != JsonValueKind.Array)
		{
			throw new ArgumentException($"Tool '{toolName}' schema field 'required' must be an array.", paramName);
		}

		foreach (var requiredItem in requiredElement.EnumerateArray())
		{
			if (requiredItem.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(requiredItem.GetString()))
			{
				throw new ArgumentException($"Tool '{toolName}' schema required entries must be non-empty strings.", paramName);
			}

			var requiredProperty = requiredItem.GetString()!;
			if (!propertiesElement.TryGetProperty(requiredProperty, out _))
			{
				throw new ArgumentException($"Tool '{toolName}' schema required property '{requiredProperty}' is missing from properties.", paramName);
			}
		}
	}

	private static string? GetCorrelationId(HttpResponseMessage response)
	{
		return TryReadHeader(response, "x-request-id")
			?? TryReadHeader(response, "x-correlation-id")
			?? TryReadHeader(response, "traceparent");
	}

	private static string? TryReadHeader(HttpResponseMessage response, string headerName)
	{
		if (response.Headers.TryGetValues(headerName, out var headerValues))
		{
			return headerValues.FirstOrDefault();
		}

		if (response.Content.Headers.TryGetValues(headerName, out var contentHeaderValues))
		{
			return contentHeaderValues.FirstOrDefault();
		}

		return null;
	}

	private static string ShortenForLog(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		return value.Length <= MaxLoggedProviderResponseLength
			? value
			: value[..MaxLoggedProviderResponseLength] + "...";
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
