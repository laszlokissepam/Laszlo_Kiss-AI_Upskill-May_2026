using GardenBuddy.Application.Products;
using GardenBuddy.Domain.Entities;
using GardenBuddy.Infrastructure.Persistence;
using GardenBuddy.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GardenBuddy.Tests;

public class ProductServiceTests
{
	[Fact]
	public async Task SearchAsync_MatchesProductNameCaseInsensitively_InSqlite()
	{
		await using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var options = new DbContextOptionsBuilder<GardenBuddyDbContext>()
			.UseSqlite(connection)
			.Options;

		await using var dbContext = new GardenBuddyDbContext(options);
		await dbContext.Database.EnsureCreatedAsync();
		dbContext.Products.Add(new Product
		{
			Name = "Lavender",
			Category = "Plant",
			Description = "Fragrant perennial herb.",
			Price = 12.99m,
			StockQuantity = 18,
			SunlightRequirement = "Full Sun",
			WateringRequirement = "Low",
			Difficulty = "Beginner",
			IsIndoor = false,
			IsPerennial = true,
			PetSafetyInfo = "Non-toxic"
		});
		await dbContext.SaveChangesAsync();

		var service = new ProductService(dbContext);
		var results = await service.SearchAsync(new ProductSearchCriteria(Name: "lavender"));

		var product = Assert.Single(results);
		Assert.Equal("Lavender", product.Name);
		Assert.Equal(12.99m, product.Price);
		Assert.Equal(18, product.StockQuantity);
	}
}
