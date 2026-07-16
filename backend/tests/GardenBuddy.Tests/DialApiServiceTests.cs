using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GardenBuddy.Application.Configuration;
using GardenBuddy.Application.Dial;
using GardenBuddy.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace GardenBuddy.Tests;

public class DialApiServiceTests
{
	[Fact]
	public async Task SendChatCompletionRequestAsync_BuildsExpectedRequestAndParsesResponse()
	{
		const string expectedJsonResponse = """
			{
			  "id": "chatcmpl-123",
			  "model": "gpt-4",
			  "choices": [
				{
				  "index": 0,
				  "message": { "role": "assistant", "content": "Try lavender and marigold." },
				  "finish_reason": "stop"
				}
			  ]
			}
			""";

		HttpRequestMessage? capturedRequest = null;
		var handler = new FakeHttpMessageHandler(request =>
		{
			capturedRequest = request;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(expectedJsonResponse, Encoding.UTF8, "application/json")
			};
		});

		var options = CreateOptions();
		var service = CreateService(handler, options);

		var messages = new[]
		{
			new DialChatMessage("user", "What are beginner-friendly plants?")
		};

		var result = await service.SendChatCompletionRequestAsync(
			deploymentName: "gpt-4-turbo-deployment",
			messages: messages,
			temperature: 0.5,
			maxTokens: 200);

		Assert.NotNull(capturedRequest);
		Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
		Assert.Equal("https://dialx.ai/api/v1/openai/deployments/gpt-4-turbo-deployment/chat/completions?api-version=2024-10-21", capturedRequest.RequestUri!.ToString());
		Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
		Assert.Equal("unit-test-key", capturedRequest.Headers.Authorization?.Parameter);
		Assert.True(capturedRequest.Headers.TryGetValues("X-CACHE-POLICY", out var cacheValues));
		Assert.Contains("availability-priority", cacheValues!);

		var requestContent = await capturedRequest.Content!.ReadAsStringAsync();
		Assert.Contains("\"model\":\"gpt-4\"", requestContent);
		Assert.Contains("\"temperature\":0.5", requestContent);
		Assert.Contains("\"top_p\":1", requestContent);
		Assert.Contains("\"max_tokens\":200", requestContent);

		Assert.Equal("chatcmpl-123", result.Id);
		Assert.Single(result.Choices);
		Assert.Equal("Try lavender and marigold.", result.Choices.First().Message.Content);
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_IncludesToolsAndToolChoice_WhenProvided()
	{
		HttpRequestMessage? capturedRequest = null;
		var handler = new FakeHttpMessageHandler(request =>
		{
			capturedRequest = request;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{\"id\":\"id\",\"model\":\"gpt-4\",\"choices\":[]}", Encoding.UTF8, "application/json")
			};
		});

		var options = CreateOptions();
		var service = CreateService(handler, options);

		var tools = new[]
		{
			new DialToolDefinition(
				"function",
				new DialToolDefinitionFunction(
					"SearchProducts",
					"Search products from catalog",
					new
					{
						type = "object",
						properties = new
						{
							category = new { type = "string" }
						}
					}))
		};

		await service.SendChatCompletionRequestAsync(
			"deployment",
			new[] { new DialChatMessage("user", "find sunny plants") },
			0.2,
			120,
			tools,
			"auto");

		Assert.NotNull(capturedRequest);
		var requestContent = await capturedRequest!.Content!.ReadAsStringAsync();
		Assert.Contains("\"tools\":[", requestContent);
		Assert.Contains("\"name\":\"SearchProducts\"", requestContent);
		Assert.Contains("\"tool_choice\":\"auto\"", requestContent);
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_ParsesToolCalls_FromProviderResponse()
	{
		const string responseWithToolCall = """
			{
			  "id": "chatcmpl-tool",
			  "model": "gpt-4",
			  "choices": [
				{
				  "index": 0,
				  "message": {
					"role": "assistant",
					"content": null,
					"tool_calls": [
					  {
						"id": "call_1",
						"type": "function",
						"function": {
						  "name": "SearchProducts",
						  "arguments": "{\"category\":\"Plant\"}"
						}
					  }
					]
				  },
				  "finish_reason": "tool_calls"
				}
			  ]
			}
			""";

		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(responseWithToolCall, Encoding.UTF8, "application/json")
			});

		var service = CreateService(handler, CreateOptions());
		var response = await service.SendChatCompletionRequestAsync(
			"deployment",
			new[] { new DialChatMessage("user", "hello") },
			0.1,
			64);

		var toolCall = response.Choices.First().Message.ToolCalls!.First();
		Assert.Equal("SearchProducts", toolCall.Function.Name);
		Assert.Equal("{\"category\":\"Plant\"}", toolCall.Function.Arguments);
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_Throws_WhenResponsePayloadIsInvalidJson()
	{
		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("not-json", Encoding.UTF8, "application/json")
			});

		var service = CreateService(handler, CreateOptions());

		await Assert.ThrowsAsync<JsonException>(() =>
			service.SendChatCompletionRequestAsync("deployment", new[] { new DialChatMessage("user", "hello") }, 0.2, 64));
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_UsesEnvironmentApiKey_WhenPresent()
	{
		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{\"id\":\"id\",\"model\":\"gpt-4\",\"choices\":[]}", Encoding.UTF8, "application/json")
			});

		var options = CreateOptions();
		Environment.SetEnvironmentVariable(options.ApiKeyEnvironmentVariable, "env-priority-key");

		try
		{
			var service = CreateService(handler, options);
			await service.SendChatCompletionRequestAsync("deployment", new[] { new DialChatMessage("user", "hello") }, 0.1, 50);
			var sentRequest = handler.Requests.Single();

			Assert.Equal("env-priority-key", sentRequest.Headers.Authorization?.Parameter);
		}
		finally
		{
			Environment.SetEnvironmentVariable(options.ApiKeyEnvironmentVariable, null);
		}
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_ThrowsUnauthorizedException_For401()
	{
		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.Unauthorized)
			{
				Content = new StringContent("invalid token", Encoding.UTF8, "application/json")
			});

		var service = CreateService(handler, CreateOptions());

		var exception = await Assert.ThrowsAsync<DialApiException>(() =>
			service.SendChatCompletionRequestAsync("deployment", new[] { new DialChatMessage("user", "hello") }, 0.2, 100));

		Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
		Assert.Contains("401", exception.Message);
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_ThrowsBadRequestException_For400()
	{
		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.BadRequest)
			{
				Content = new StringContent("missing parameters", Encoding.UTF8, "application/json")
			});

		var service = CreateService(handler, CreateOptions());

		var exception = await Assert.ThrowsAsync<DialApiException>(() =>
			service.SendChatCompletionRequestAsync("deployment", new[] { new DialChatMessage("user", "hello") }, 0.2, 100));

		Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
		Assert.Contains("400", exception.Message);
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_ThrowsInternalServerErrorException_For500()
	{
		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.InternalServerError)
			{
				Content = new StringContent("server error", Encoding.UTF8, "application/json")
			});

		var service = CreateService(handler, CreateOptions());

		var exception = await Assert.ThrowsAsync<DialApiException>(() =>
			service.SendChatCompletionRequestAsync("deployment", new[] { new DialChatMessage("user", "hello") }, 0.2, 100));

		Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
		Assert.Contains("500", exception.Message);
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_Throws_WhenMessagesAreMissing()
	{
		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{\"id\":\"id\",\"model\":\"gpt-4\",\"choices\":[]}", Encoding.UTF8, "application/json")
			});

		var service = CreateService(handler, CreateOptions());

		await Assert.ThrowsAsync<ArgumentException>(() =>
			service.SendChatCompletionRequestAsync("deployment", Array.Empty<DialChatMessage>(), 0.2, 100));
	}

	[Fact]
	public async Task SendChatCompletionRequestAsync_Throws_WhenApiKeyMissing()
	{
		var handler = new FakeHttpMessageHandler(_ =>
			new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("{\"id\":\"id\",\"model\":\"gpt-4\",\"choices\":[]}", Encoding.UTF8, "application/json")
			});

		var options = CreateOptions();
		options.ApiKey = string.Empty;
		Environment.SetEnvironmentVariable(options.ApiKeyEnvironmentVariable, null);

		var service = CreateService(handler, options);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.SendChatCompletionRequestAsync("deployment", new[] { new DialChatMessage("user", "hello") }, 0.2, 100));

		Assert.Contains("DIAL API key is missing", exception.Message);
	}

	private static DialApiService CreateService(HttpMessageHandler handler, DialApiOptions options)
	{
		var client = new HttpClient(handler)
		{
			BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/")
		};

		return new DialApiService(client, Options.Create(options));
	}

	private static DialApiOptions CreateOptions() => new()
	{
		BaseUrl = "https://dialx.ai/api/v1",
		ApiVersion = "2024-10-21",
		DefaultModel = "gpt-4",
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

		public List<HttpRequestMessage> Requests { get; } = new();

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(_handler(request));
		}
	}
}
