using System.ComponentModel.DataAnnotations;

namespace GardenBuddy.Infrastructure.Configuration;

public sealed class KnowledgeOptions
{
	public const string SectionName = "Knowledge";

	[Required]
	public string DocumentsPath { get; set; } = "data/knowledge";

	[Required]
	public string EmbeddingCachePath { get; set; } = "backend/src/GardenBuddy.Api/knowledge-embeddings-cache.json";

	[Range(200, 4000)]
	public int ChunkSize { get; set; } = 800;

	[Range(50, 1000)]
	public int ChunkOverlap { get; set; } = 150;
}
