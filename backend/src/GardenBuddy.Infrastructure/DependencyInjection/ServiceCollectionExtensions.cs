using System.Net.Http.Headers;
using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Configuration;
using GardenBuddy.Infrastructure.Configuration;
using GardenBuddy.Infrastructure.Persistence;
using GardenBuddy.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

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

		services
			.AddOptions<KnowledgeOptions>()
			.Bind(configuration.GetSection(KnowledgeOptions.SectionName))
			.ValidateDataAnnotations();

		var connectionString = configuration.GetConnectionString("DefaultConnection")
			?? "Data Source=data/products/gardenbuddy.dev.db";

		services.AddDbContext<GardenBuddyDbContext>(options => options.UseSqlite(connectionString));

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

		services.AddHttpClient<IEmbeddingService, DialEmbeddingService>((serviceProvider, client) =>
		{
			var options = serviceProvider
				.GetRequiredService<Microsoft.Extensions.Options.IOptions<DialApiOptions>>()
				.Value;

			client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
			client.DefaultRequestHeaders.Accept.Clear();
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		});

		services.AddSingleton<IKnowledgeBaseService, KnowledgeBaseService>();
		services.AddScoped<IProductService, ProductService>();

		return services;
	}

	private static string ResolveApiKey(DialApiOptions options)
	{
		var environmentKey = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);
		return string.IsNullOrWhiteSpace(environmentKey) ? options.ApiKey : environmentKey;
	}
}
