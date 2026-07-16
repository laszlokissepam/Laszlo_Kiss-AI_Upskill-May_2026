namespace GardenBuddy.Domain.Entities;

public sealed class Product
{
	public int Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Category { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public decimal Price { get; set; }
	public int StockQuantity { get; set; }
	public string SunlightRequirement { get; set; } = string.Empty;
	public string WateringRequirement { get; set; } = string.Empty;
	public string Difficulty { get; set; } = string.Empty;
	public bool IsIndoor { get; set; }
	public bool IsPerennial { get; set; }
	public string PetSafetyInfo { get; set; } = string.Empty;
}
