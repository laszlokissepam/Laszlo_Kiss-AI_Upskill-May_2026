using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Knowledge;
using GardenBuddy.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GardenBuddy.Infrastructure.Services;

public sealed partial class KnowledgeBaseService : IKnowledgeBaseService
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

	private readonly IEmbeddingService _embeddingService;
	private readonly KnowledgeOptions _options;
	private readonly SemaphoreSlim _lock = new(1, 1);

	private readonly Dictionary<string, CachedEmbedding> _embeddingCache = new(StringComparer.Ordinal);
	private readonly List<KnowledgeChunk> _chunks = [];
	private bool _cacheLoaded;

	public KnowledgeBaseService(IEmbeddingService embeddingService, IOptions<KnowledgeOptions> options)
	{
		_embeddingService = embeddingService;
		_options = options.Value;
	}

	public async Task<KnowledgeIngestionResult> IngestMarkdownDocumentsAsync(CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			return await IngestUnderLockAsync(cancellationToken);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<IReadOnlyCollection<KnowledgeSearchResult>> SearchAsync(
		string query,
		int topK = 5,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(query))
		{
			throw new ArgumentException("Search query is required.", nameof(query));
		}

		if (topK <= 0 || topK > 20)
		{
			throw new ArgumentOutOfRangeException(nameof(topK), "topK must be between 1 and 20.");
		}

		await _lock.WaitAsync(cancellationToken);
		try
		{
			if (_chunks.Count == 0)
			{
				await IngestUnderLockAsync(cancellationToken);
			}

			if (_chunks.Count == 0)
			{
				return Array.Empty<KnowledgeSearchResult>();
			}

			var queryVector = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
			var results = _chunks
				.Select(chunk =>
				{
					var score = CosineSimilarity(chunk.Embedding, queryVector);
					return new KnowledgeSearchResult(chunk.Source, chunk.ChunkId, chunk.Content, score);
				})
				.OrderByDescending(result => result.Score)
				.Take(topK)
				.ToArray();

			return results;
		}
		finally
		{
			_lock.Release();
		}
	}

	private async Task<KnowledgeIngestionResult> IngestUnderLockAsync(CancellationToken cancellationToken)
	{
		await EnsureCacheLoadedAsync(cancellationToken);

		var documentsPath = Path.GetFullPath(_options.DocumentsPath);
		if (!Directory.Exists(documentsPath))
		{
			_chunks.Clear();
			return new KnowledgeIngestionResult(0, 0, 0, 0);
		}

		var files = Directory.GetFiles(documentsPath, "*.md", SearchOption.AllDirectories);
		_chunks.Clear();

		var embeddingsGenerated = 0;
		var embeddingsReused = 0;
		var chunksCreated = 0;

		foreach (var file in files)
		{
			var markdown = await File.ReadAllTextAsync(file, cancellationToken);
			var source = Path.GetRelativePath(documentsPath, file).Replace('\\', '/');

			var chunkIndex = 0;
			foreach (var chunkText in SplitMarkdownIntoChunks(markdown))
			{
				chunkIndex++;
				chunksCreated++;
				var chunkId = $"{source}#chunk-{chunkIndex}";
				var chunkHash = ComputeHash(chunkText);

				IReadOnlyList<float> embedding;
				if (_embeddingCache.TryGetValue(chunkHash, out var cached))
				{
					embedding = cached.Vector;
					embeddingsReused++;
				}
				else
				{
					embedding = await _embeddingService.GenerateEmbeddingAsync(chunkText, cancellationToken);
					_embeddingCache[chunkHash] = new CachedEmbedding(chunkHash, embedding);
					embeddingsGenerated++;
				}

				_chunks.Add(new KnowledgeChunk(chunkId, source, chunkText, embedding));
			}
		}

		await PersistCacheAsync(cancellationToken);
		return new KnowledgeIngestionResult(files.Length, chunksCreated, embeddingsGenerated, embeddingsReused);
	}

	private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
	{
		if (_cacheLoaded)
		{
			return;
		}

		var cachePath = Path.GetFullPath(_options.EmbeddingCachePath);
		if (File.Exists(cachePath))
		{
			var json = await File.ReadAllTextAsync(cachePath, cancellationToken);
			var items = JsonSerializer.Deserialize<List<CachedEmbedding>>(json, JsonOptions) ?? [];
			foreach (var item in items)
			{
				_embeddingCache[item.Hash] = item;
			}
		}

		_cacheLoaded = true;
	}

	private async Task PersistCacheAsync(CancellationToken cancellationToken)
	{
		var cachePath = Path.GetFullPath(_options.EmbeddingCachePath);
		var directory = Path.GetDirectoryName(cachePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var payload = _embeddingCache.Values.OrderBy(item => item.Hash, StringComparer.Ordinal).ToArray();
		await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
	}

	private IEnumerable<string> SplitMarkdownIntoChunks(string markdown)
	{
		if (string.IsNullOrWhiteSpace(markdown))
		{
			yield break;
		}

		var normalized = markdown.Replace("\r\n", "\n").Trim();
		var plainText = MarkdownCleanupRegex().Replace(normalized, "$1").Trim();
		if (plainText.Length == 0)
		{
			yield break;
		}

		var chunkSize = _options.ChunkSize;
		var overlap = Math.Min(_options.ChunkOverlap, chunkSize / 2);
		var start = 0;
		while (start < plainText.Length)
		{
			var length = Math.Min(chunkSize, plainText.Length - start);
			var end = start + length;
			if (end < plainText.Length)
			{
				var lastSpace = plainText.LastIndexOf(' ', end - 1, length);
				if (lastSpace > start + (chunkSize / 2))
				{
					end = lastSpace;
					length = end - start;
				}
			}

			var chunk = plainText.Substring(start, length).Trim();
			if (chunk.Length > 0)
			{
				yield return chunk;
			}

			if (end >= plainText.Length)
			{
				break;
			}

			start = Math.Max(end - overlap, start + 1);
		}
	}

	private static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
	{
		if (a.Count == 0 || b.Count == 0 || a.Count != b.Count)
		{
			return 0;
		}

		double dot = 0;
		double normA = 0;
		double normB = 0;
		for (var i = 0; i < a.Count; i++)
		{
			dot += a[i] * b[i];
			normA += a[i] * a[i];
			normB += b[i] * b[i];
		}

		if (normA == 0 || normB == 0)
		{
			return 0;
		}

		return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
	}

	private static string ComputeHash(string value)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		return Convert.ToHexString(bytes);
	}

	[GeneratedRegex(@"(?m)^#{1,6}\s*|`|\*\*?|\[(.*?)\]\((.*?)\)")]
	private static partial Regex MarkdownCleanupRegex();

	private sealed record KnowledgeChunk(string ChunkId, string Source, string Content, IReadOnlyList<float> Embedding);

	private sealed record CachedEmbedding(string Hash, IReadOnlyList<float> Vector);
}
