namespace CSharpApp.Core.Interfaces;

/// <summary>Reads products from the upstream catalogue.</summary>
public interface IProductsService
{
    Task<IReadOnlyCollection<Product>> GetProducts();
}
