using GardenBuddy.Application.Knowledge;

namespace GardenBuddy.Application.Abstractions;

public interface IKnowledgeBaseService
{
	Task<KnowledgeIngestionResult> IngestMarkdownDocumentsAsync(CancellationToken cancellationToken = default);

	Task<IReadOnlyCollection<KnowledgeSearchResult>> SearchAsync(
		string query,
		int topK = 5,
		CancellationToken cancellationToken = default);
}
