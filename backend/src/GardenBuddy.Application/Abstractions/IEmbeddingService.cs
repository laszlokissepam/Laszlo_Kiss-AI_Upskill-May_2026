namespace GardenBuddy.Application.Abstractions;

public interface IEmbeddingService
{
	Task<IReadOnlyList<float>> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default);
}
