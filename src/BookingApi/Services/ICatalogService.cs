using BookingApi.Dtos;

namespace BookingApi.Services;

public interface ICatalogService
{
    Task<IReadOnlyList<ProviderResponse>> GetProvidersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ServiceResponse>> GetServicesAsync(CancellationToken cancellationToken);
}
