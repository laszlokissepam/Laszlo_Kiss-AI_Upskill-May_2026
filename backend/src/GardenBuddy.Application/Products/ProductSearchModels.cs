namespace GardenBuddy.Application.Products;

public sealed record ProductSearchCriteria(
	string? Name = null,
	string? Category = null,
	string? SunlightRequirement = null,
	string? Difficulty = null,
	decimal? MinPrice = null,
	decimal? MaxPrice = null,
	bool? InStockOnly = null,
	int TopK = 5);

public sealed record ProductSearchResult(
	int Id,
	string Name,
	string Category,
	string Description,
	decimal Price,
	int StockQuantity,
	string SunlightRequirement,
	string WateringRequirement,
	string Difficulty,
	bool IsIndoor,
	bool IsPerennial,
	string PetSafetyInfo);
