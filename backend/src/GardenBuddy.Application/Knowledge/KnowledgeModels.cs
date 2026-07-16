namespace GardenBuddy.Application.Knowledge;

public sealed record KnowledgeIngestionResult(
	int FilesProcessed,
	int ChunksCreated,
	int EmbeddingsGenerated,
	int EmbeddingsReused);

public sealed record KnowledgeSearchResult(
	string Source,
	string ChunkId,
	string Content,
	double Score);
