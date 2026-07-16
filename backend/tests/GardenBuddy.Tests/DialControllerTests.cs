using System.Net;
using GardenBuddy.Api.Controllers;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Dial;
using Microsoft.AspNetCore.Mvc;

namespace GardenBuddy.Tests;

public class DialControllerTests
{
    [Fact]
    public async Task ChatCompletionAsync_ReturnsBadRequest_WhenArgumentsInvalid()
    {
        var service = new FakeDialApiService
        {
            ExceptionToThrow = new ArgumentException("invalid")
        };
        var controller = new DialController(service);

        var result = await controller.ChatCompletionAsync(
            new DialChatCompletionApiRequest("deployment", new[] { new DialChatMessage("user", "hi") }),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ChatCompletionAsync_MapsDialApiExceptionStatusCode()
    {
        var service = new FakeDialApiService
        {
            ExceptionToThrow = new DialApiException(HttpStatusCode.Unauthorized, "unauthorized")
        };
        var controller = new DialController(service);

        var result = await controller.ChatCompletionAsync(
            new DialChatCompletionApiRequest("deployment", new[] { new DialChatMessage("user", "hi") }),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, objectResult.StatusCode);
    }

    private sealed class FakeDialApiService : IDialApiService
    {
        public Exception? ExceptionToThrow { get; set; }

        public Task<DialChatCompletionResponse> SendChatCompletionRequestAsync(
            string deploymentName,
            IReadOnlyCollection<DialChatMessage> messages,
            double temperature,
            int maxTokens,
            IReadOnlyCollection<DialToolDefinition>? tools = null,
            string? toolChoice = null,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(new DialChatCompletionResponse(
                "ok",
                "gpt-4",
                new[]
                {
                    new DialChatCompletionChoice(0, new DialChatMessage("assistant", "ok"), "stop")
                }));
        }
    }
}
