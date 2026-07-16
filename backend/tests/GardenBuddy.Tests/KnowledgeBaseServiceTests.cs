using GardenBuddy.Application.Abstractions;
using GardenBuddy.Infrastructure.Configuration;
using GardenBuddy.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace GardenBuddy.Tests;

public class KnowledgeBaseServiceTests
{
	[Fact]
	public async Task IngestMarkdownDocumentsAsync_SplitsAndGeneratesEmbeddings()
	{
		var workspace = CreateWorkspace();
		try
		{
			var docsPath = Path.Combine(workspace, "docs");
			Directory.CreateDirectory(docsPath);
			await File.WriteAllTextAsync(Path.Combine(docsPath, "policy.md"), "# Delivery\nHome delivery is available for local orders.");
			await File.WriteAllTextAsync(Path.Combine(docsPath, "care.md"), "# Lavender\nLavender likes full sun and low watering.");

			var embeddingService = new FakeEmbeddingService();
			var service = CreateService(embeddingService, docsPath, Path.Combine(workspace, "cache.json"));

			var result = await service.IngestMarkdownDocumentsAsync();

			Assert.Equal(2, result.FilesProcessed);
			Assert.True(result.ChunksCreated >= 2);
			Assert.True(result.EmbeddingsGenerated >= 2);
		}
		finally
		{
			Directory.Delete(workspace, recursive: true);
		}
	}

	[Fact]
	public async Task IngestMarkdownDocumentsAsync_ReusesCachedEmbeddings_OnSecondRun()
	{
		var workspace = CreateWorkspace();
		try
		{
			var docsPath = Path.Combine(workspace, "docs");
			Directory.CreateDirectory(docsPath);
			await File.WriteAllTextAsync(Path.Combine(docsPath, "policy.md"), "Home delivery is available.");

			var embeddingService = new FakeEmbeddingService();
			var service = CreateService(embeddingService, docsPath, Path.Combine(workspace, "cache.json"));

			var first = await service.IngestMarkdownDocumentsAsync();
			var callsAfterFirst = embeddingService.CallCount;
			var second = await service.IngestMarkdownDocumentsAsync();

			Assert.True(first.EmbeddingsGenerated > 0);
			Assert.Equal(callsAfterFirst, embeddingService.CallCount);
			Assert.True(second.EmbeddingsReused > 0);
		}
		finally
		{
			Directory.Delete(workspace, recursive: true);
		}
	}

	[Fact]
	public async Task SearchAsync_ReturnsMostRelevantChunk_ByCosineSimilarity()
	{
		var workspace = CreateWorkspace();
		try
		{
			var docsPath = Path.Combine(workspace, "docs");
			Directory.CreateDirectory(docsPath);
			await File.WriteAllTextAsync(Path.Combine(docsPath, "policy.md"), "Home delivery is available for in-stock products.");
			await File.WriteAllTextAsync(Path.Combine(docsPath, "care.md"), "Lavender prefers full sun.");

			var embeddingService = new FakeEmbeddingService();
			var service = CreateService(embeddingService, docsPath, Path.Combine(workspace, "cache.json"));
			await service.IngestMarkdownDocumentsAsync();

			var results = await service.SearchAsync("Do you offer delivery?", topK: 1);
			var best = Assert.Single(results);

			Assert.Equal("policy.md", best.Source);
			Assert.Contains("delivery", best.Content, StringComparison.OrdinalIgnoreCase);
			Assert.True(best.Score > 0.5);
		}
		finally
		{
			Directory.Delete(workspace, recursive: true);
		}
	}

	private static KnowledgeBaseService CreateService(
		IEmbeddingService embeddingService,
		string documentsPath,
		string cachePath)
	{
		var options = Options.Create(new KnowledgeOptions
		{
			DocumentsPath = documentsPath,
			EmbeddingCachePath = cachePath,
			ChunkSize = 1000,
			ChunkOverlap = 100
		});

		return new KnowledgeBaseService(embeddingService, options);
	}

	private static string CreateWorkspace()
	{
		var path = Path.Combine(Path.GetTempPath(), "knowledge-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private sealed class FakeEmbeddingService : IEmbeddingService
	{
		public int CallCount { get; private set; }

		public Task<IReadOnlyList<float>> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default)
		{
			CallCount++;
			var lower = input.ToLowerInvariant();
			IReadOnlyList<float> vector = lower switch
			{
				_ when lower.Contains("delivery") => new float[] { 1f, 0f, 0f },
				_ when lower.Contains("lavender") => new float[] { 0f, 1f, 0f },
				_ => new float[] { 0f, 0f, 1f }
			};

			return Task.FromResult(vector);
		}
	}
}
