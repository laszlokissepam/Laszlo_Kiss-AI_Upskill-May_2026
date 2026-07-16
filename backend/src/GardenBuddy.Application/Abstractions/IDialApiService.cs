using GardenBuddy.Application.Dial;

namespace GardenBuddy.Application.Abstractions;

public interface IDialApiService
{
	Task<DialChatCompletionResponse> SendChatCompletionRequestAsync(
		string deploymentName,
		IReadOnlyCollection<DialChatMessage> messages,
		double temperature,
		int maxTokens,
		IReadOnlyCollection<DialToolDefinition>? tools = null,
		string? toolChoice = null,
		CancellationToken cancellationToken = default);
}
