using System.Text.Json.Serialization;

namespace GardenBuddy.Application.Dial;

public sealed record DialEmbeddingRequest(
	[property: JsonPropertyName("model")] string Model,
	[property: JsonPropertyName("input")] string Input);

public sealed record DialEmbeddingItem(
	[property: JsonPropertyName("index")] int Index,
	[property: JsonPropertyName("embedding")] IReadOnlyList<float> Embedding);

public sealed record DialEmbeddingResponse(
	[property: JsonPropertyName("data")] IReadOnlyCollection<DialEmbeddingItem> Data);
