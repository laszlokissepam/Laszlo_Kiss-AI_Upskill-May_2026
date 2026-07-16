using System.Net;
using GardenBuddy.Api.Controllers;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Dial;
using GardenBuddy.Application.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace GardenBuddy.Tests;

public class ChatControllerTests
{
	[Fact]
	public async Task PostAsync_ExecutesKnowledgeTool_AndReturnsFinalAnswerWithSources()
	{
		var dial = new FakeDialApiService();
		dial.QueueResponse(new DialChatCompletionResponse(
			"1",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(
					0,
					new DialChatMessage(
						"assistant",
						null,
						new[]
						{
							new DialToolCall(
								"call_1",
								"function",
								new DialToolFunction("SearchKnowledgeBase", "{\"query\":\"home delivery\",\"topK\":2}"))
						}),
					"tool_calls")
			}));
		dial.QueueResponse(new DialChatCompletionResponse(
			"2",
			"gpt-4",
			new[]
			{
				new DialChatCompletionChoice(0, new DialChatMessage("assistant", "We offer local home delivery."), "stop")
			}));

		var knowledge = new FakeKnowledgeBaseService
		{
			Results = new[]
			{
				new KnowledgeSearchResult("store-policies.md", "store-policies.md#chunk-1", "Home delivery is available", 0.91)
			}
		};

		var controller = new ChatController(dial, knowledge);
		var result = await controller.PostAsync(new ChatRequest("gpt-4-turbo-deployment", "Do you offer home delivery?"), CancellationToken.None);

		var ok = Assert.IsType<OkObjectResult>(result);
		var payload = Assert.IsType<ChatResponse>(ok.Value);
		Assert.Contains("home delivery", payload.Answer, StringComparison.OrdinalIgnoreCase);
		Assert.Single(payload.Sources);
		Assert.Equal("store-policies.md", payload.Sources.First().Source);
		Assert.Equal(2, dial.CallCount);
	}

	[Fact]
	public async Task PostAsync_ReturnsBadRequest_WhenMessageMissing()
	{
		var controller = new ChatController(new FakeDialApiService(), new FakeKnowledgeBaseService());
		var result = await controller.PostAsync(new ChatRequest("dep", ""), CancellationToken.None);

		Assert.IsType<BadRequestObjectResult>(result);
	}

	private sealed class FakeDialApiService : IDialApiService
	{
		private readonly Queue<DialChatCompletionResponse> _responses = new();

		public int CallCount { get; private set; }

		public void QueueResponse(DialChatCompletionResponse response)
		{
			_responses.Enqueue(response);
		}

		public Task<DialChatCompletionResponse> SendChatCompletionRequestAsync(
			string deploymentName,
			IReadOnlyCollection<DialChatMessage> messages,
			double temperature,
			int maxTokens,
			IReadOnlyCollection<DialToolDefinition>? tools = null,
			object? toolChoice = null,
			bool? parallelToolCalls = null,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			if (_responses.Count == 0)
			{
				throw new DialApiException(HttpStatusCode.InternalServerError, "No fake response configured");
			}

			return Task.FromResult(_responses.Dequeue());
		}
	}

	private sealed class FakeKnowledgeBaseService : IKnowledgeBaseService
	{
		public IReadOnlyCollection<KnowledgeSearchResult> Results { get; set; } = Array.Empty<KnowledgeSearchResult>();

		public Task<KnowledgeIngestionResult> IngestMarkdownDocumentsAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new KnowledgeIngestionResult(0, 0, 0, 0));
		}

		public Task<IReadOnlyCollection<KnowledgeSearchResult>> SearchAsync(string query, int topK = 5, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(Results);
		}
	}
}
