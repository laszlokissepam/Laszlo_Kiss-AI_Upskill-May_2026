using GardenBuddy.Application.Products;

namespace GardenBuddy.Application.Abstractions;

public interface IProductService
{
	Task<IReadOnlyCollection<ProductSearchResult>> SearchAsync(
		ProductSearchCriteria criteria,
		CancellationToken cancellationToken = default);
}
