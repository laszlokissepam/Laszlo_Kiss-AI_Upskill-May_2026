using System.Net;
using System.Net.Http;
using System.Text;
using GardenBuddy.Application.Configuration;
using GardenBuddy.Application.Dial;
using GardenBuddy.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace GardenBuddy.Tests;

public class DialEmbeddingServiceTests
{
	[Fact]
	public async Task GenerateEmbeddingAsync_BuildsExpectedRequest_AndParsesVector()
	{
		HttpRequestMessage? capturedRequest = null;
		var handler = new FakeHttpMessageHandler(request =>
		{
			capturedRequest = request;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{" +
					"\"data\":[{" +
					"\"index\":0," +
					"\"embedding\":[0.1,0.2,0.3]" +
					"}]" +
					"}", Encoding.UTF8, "application/json")
			};
		});

		var options = CreateOptions();
		var service = CreateService(handler, options);
		var vector = await service.GenerateEmbeddingAsync("home delivery policy");

		Assert.NotNull(capturedRequest);
		Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
		Assert.Equal("https://dialx.ai/api/v1/openai/deployments/text-embedding-3-small/embeddings?api-version=2024-10-21", capturedRequest.RequestUri!.ToString());
		Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
		Assert.Equal("unit-test-key", capturedRequest.Headers.Authorization?.Parameter);

		var requestBody = handler.RequestBodies.Single();
		Assert.Contains("\"model\":\"text-embedding-3-small\"", requestBody);
		Assert.Contains("\"input\":\"home delivery policy\"", requestBody);

		Assert.Equal(3, vector.Count);
		Assert.Equal(0.1f, vector[0]);
	}

	[Fact]
	public async Task GenerateEmbeddingAsync_ThrowsDialApiException_ForUnauthorized()
	{
		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.Unauthorized)
			{
				Content = new StringContent("invalid token", Encoding.UTF8, "application/json")
			});

		var service = CreateService(handler, CreateOptions());
		var ex = await Assert.ThrowsAsync<DialApiException>(() => service.GenerateEmbeddingAsync("home delivery"));

		Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
	}

	private static DialEmbeddingService CreateService(HttpMessageHandler handler, DialApiOptions options)
	{
		var client = new HttpClient(handler)
		{
			BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/")
		};

		return new DialEmbeddingService(client, Options.Create(options));
	}

	private static DialApiOptions CreateOptions() => new()
	{
		BaseUrl = "https://dialx.ai/api/v1",
		ApiVersion = "2024-10-21",
		DefaultModel = "gpt-4",
		EmbeddingModel = "text-embedding-3-small",
		EmbeddingDeploymentName = "text-embedding-3-small",
		CachePolicy = "availability-priority",
		ApiKeyEnvironmentVariable = "DIAL_API_KEY_UNIT_TEST",
		ApiKey = "unit-test-key"
	};

	private sealed class FakeHttpMessageHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

		public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
		{
			_handler = handler;
		}

		public List<string> RequestBodies { get; } = new();

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Content is not null)
			{
				RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
			}

			return _handler(request);
		}
	}
}
