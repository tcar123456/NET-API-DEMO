using BookingApi.Domain;

namespace BookingApi.Repositories;

public interface IProviderRepository
{
    Task<IReadOnlyList<Provider>> GetActiveAsync(CancellationToken cancellationToken);

    Task<Provider?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
