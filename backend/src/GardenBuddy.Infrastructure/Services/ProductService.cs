using GardenBuddy.Application.Abstractions;
using GardenBuddy.Application.Products;
using GardenBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GardenBuddy.Infrastructure.Services;

public sealed class ProductService : IProductService
{
	private readonly GardenBuddyDbContext _dbContext;

	public ProductService(GardenBuddyDbContext dbContext)
	{
		_dbContext = dbContext;
	}

	public async Task<IReadOnlyCollection<ProductSearchResult>> SearchAsync(
		ProductSearchCriteria criteria,
		CancellationToken cancellationToken = default)
	{
		if (criteria.TopK <= 0 || criteria.TopK > 20)
		{
			throw new ArgumentOutOfRangeException(nameof(criteria.TopK), "TopK must be between 1 and 20.");
		}

		var query = _dbContext.Products.AsNoTracking().AsQueryable();

		if (!string.IsNullOrWhiteSpace(criteria.Name))
		{
			var name = criteria.Name.Trim();
			query = query.Where(product => product.Name.Contains(name));
		}

		if (!string.IsNullOrWhiteSpace(criteria.Category))
		{
			var category = criteria.Category.Trim();
			query = query.Where(product => product.Category == category);
		}

		if (!string.IsNullOrWhiteSpace(criteria.SunlightRequirement))
		{
			var sunlight = criteria.SunlightRequirement.Trim();
			query = query.Where(product => product.SunlightRequirement == sunlight);
		}

		if (!string.IsNullOrWhiteSpace(criteria.Difficulty))
		{
			var difficulty = criteria.Difficulty.Trim();
			query = query.Where(product => product.Difficulty == difficulty);
		}

		if (criteria.MinPrice.HasValue)
		{
			query = query.Where(product => product.Price >= criteria.MinPrice.Value);
		}

		if (criteria.MaxPrice.HasValue)
		{
			query = query.Where(product => product.Price <= criteria.MaxPrice.Value);
		}

		if (criteria.InStockOnly.GetValueOrDefault())
		{
			query = query.Where(product => product.StockQuantity > 0);
		}

		var products = await query
			.OrderBy(product => product.Name)
			.Take(criteria.TopK)
			.Select(product => new ProductSearchResult(
				product.Id,
				product.Name,
				product.Category,
				product.Description,
				product.Price,
				product.StockQuantity,
				product.SunlightRequirement,
				product.WateringRequirement,
				product.Difficulty,
				product.IsIndoor,
				product.IsPerennial,
				product.PetSafetyInfo))
			.ToArrayAsync(cancellationToken);

		return products;
	}
}
