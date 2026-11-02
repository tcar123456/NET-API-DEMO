using BookingApi.Dtos;
using BookingApi.Repositories;

namespace BookingApi.Services;

public class CatalogService : ICatalogService
{
    private readonly IProviderRepository _providerRepository;
    private readonly IServiceRepository _serviceRepository;

    public CatalogService(IProviderRepository providerRepository, IServiceRepository serviceRepository)
    {
        _providerRepository = providerRepository;
        _serviceRepository = serviceRepository;
    }

    public async Task<IReadOnlyList<ProviderResponse>> GetProvidersAsync(CancellationToken cancellationToken)
    {
        var providers = await _providerRepository.GetActiveAsync(cancellationToken);
        return providers.Select(p => new ProviderResponse(p.Id, p.Name)).ToList();
    }

    public async Task<IReadOnlyList<ServiceResponse>> GetServicesAsync(CancellationToken cancellationToken)
    {
        var services = await _serviceRepository.GetAllAsync(cancellationToken);
        return services.Select(s => new ServiceResponse(s.Id, s.Name, s.Price, s.DurationMinutes)).ToList();
    }
}
