using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace GardenBuddy.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
public sealed class KnowledgeController : ControllerBase
{
	private readonly IKnowledgeBaseService _knowledgeBaseService;

	public KnowledgeController(IKnowledgeBaseService knowledgeBaseService)
	{
		_knowledgeBaseService = knowledgeBaseService;
	}

	/// <summary>
	/// Ingests markdown documents, splits them into chunks, and generates/reuses embeddings.
	/// </summary>
	[HttpPost("ingest")]
	[Produces("application/json")]
	[ProducesResponseType(typeof(KnowledgeIngestionResult), StatusCodes.Status200OK)]
	public async Task<IActionResult> IngestAsync(CancellationToken cancellationToken)
	{
		var result = await _knowledgeBaseService.IngestMarkdownDocumentsAsync(cancellationToken);
		return Ok(result);
	}

	/// <summary>
	/// Performs semantic retrieval over ingested knowledge chunks.
	/// </summary>
	/// <remarks>
	/// Example request:
	/// {
	///   "query": "Do you offer home delivery?",
	///   "topK": 3
	/// }
	/// </remarks>
	[HttpPost("search")]
	[Consumes("application/json")]
	[Produces("application/json")]
	[ProducesResponseType(typeof(IReadOnlyCollection<KnowledgeSearchResult>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(KnowledgeErrorResponse), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> SearchAsync(
		[FromBody] KnowledgeSearchRequest request,
		CancellationToken cancellationToken)
	{
		try
		{
			var results = await _knowledgeBaseService.SearchAsync(request.Query, request.TopK, cancellationToken);
			return Ok(results);
		}
		catch (ArgumentException ex)
		{
			return BadRequest(new KnowledgeErrorResponse(ex.Message));
		}
	}
}

public sealed record KnowledgeSearchRequest(string Query, int TopK = 5);

public sealed record KnowledgeErrorResponse(string Error);
