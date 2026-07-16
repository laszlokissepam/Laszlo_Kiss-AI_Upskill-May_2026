using System.ComponentModel.DataAnnotations;

namespace GardenBuddy.Application.Configuration;

public sealed class DialApiOptions
{
	public const string SectionName = "Dial";

	[Required]
	public string BaseUrl { get; set; } = "https://dialx.ai/api/v1";

	[Required]
	public string ApiVersion { get; set; } = "2024-10-21";

	// Keep empty in appsettings and provide via environment variable when possible.
	public string ApiKey { get; set; } = string.Empty;

	[Required]
	public string ApiKeyEnvironmentVariable { get; set; } = "DIAL_API_KEY";

	[Required]
	public string DefaultModel { get; set; } = "gpt-4";

	[Required]
	public string CachePolicy { get; set; } = "availability-priority";
}
