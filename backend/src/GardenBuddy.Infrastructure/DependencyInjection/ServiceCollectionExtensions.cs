using System.Net.Http.Headers;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Configuration;
using GardenBuddy.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GardenBuddy.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
	{
		services
			.AddOptions<DialApiOptions>()
			.Bind(configuration.GetSection(DialApiOptions.SectionName))
			.ValidateDataAnnotations()
			.Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
				"Dial:BaseUrl must be a valid HTTPS URL.");

		services.AddHttpClient<IDialApiService, DialApiService>((serviceProvider, client) =>
		{
			var options = serviceProvider
				.GetRequiredService<Microsoft.Extensions.Options.IOptions<DialApiOptions>>()
				.Value;

			client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
			client.DefaultRequestHeaders.Accept.Clear();
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

			if (!client.DefaultRequestHeaders.Contains("X-CACHE-POLICY"))
			{
				client.DefaultRequestHeaders.Add("X-CACHE-POLICY", options.CachePolicy);
			}

			var apiKey = ResolveApiKey(options);
			if (!string.IsNullOrWhiteSpace(apiKey))
			{
				client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
			}
		});

		return services;
	}

	private static string ResolveApiKey(DialApiOptions options)
	{
		var environmentKey = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);
		return string.IsNullOrWhiteSpace(environmentKey) ? options.ApiKey : environmentKey;
	}
}
