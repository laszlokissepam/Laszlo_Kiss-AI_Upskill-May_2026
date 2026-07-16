using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Configuration;
using GardenBuddy.Application.Dial;
using Microsoft.Extensions.Options;

namespace GardenBuddy.Infrastructure.Services;

public sealed class DialEmbeddingService : IEmbeddingService
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _httpClient;
	private readonly DialApiOptions _options;

	public DialEmbeddingService(HttpClient httpClient, IOptions<DialApiOptions> options)
	{
		_httpClient = httpClient;
		_options = options.Value;
	}

	public async Task<IReadOnlyList<float>> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			throw new ArgumentException("Embedding input cannot be empty.", nameof(input));
		}

		var requestBody = new DialEmbeddingRequest(
			Model: _options.EmbeddingModel,
			Input: input);

		var endpoint = $"openai/deployments/{Uri.EscapeDataString(_options.EmbeddingDeploymentName)}/embeddings?api-version={Uri.EscapeDataString(_options.ApiVersion)}";
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

		var parsedResponse = JsonSerializer.Deserialize<DialEmbeddingResponse>(responseBody, JsonOptions);
		var vector = parsedResponse?.Data?.FirstOrDefault()?.Embedding;
		if (vector is null || vector.Count == 0)
		{
			throw new DialApiException(HttpStatusCode.InternalServerError, "DIAL API returned an empty embedding payload.", responseBody);
		}

		return vector;
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

	private static DialApiException CreateException(HttpStatusCode statusCode, string responseBody)
	{
		var message = statusCode switch
		{
			HttpStatusCode.BadRequest => "DIAL embedding request was invalid (400 Bad Request).",
			HttpStatusCode.Unauthorized => "DIAL embedding authentication failed (401 Unauthorized).",
			HttpStatusCode.InternalServerError => "DIAL embedding endpoint returned a server error (500 Internal Server Error).",
			_ => $"DIAL embedding request failed with status code {(int)statusCode}."
		};

		return new DialApiException(statusCode, message, responseBody);
	}
}
