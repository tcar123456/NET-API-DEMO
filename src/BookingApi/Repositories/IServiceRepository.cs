using BookingApi.Domain;

namespace BookingApi.Repositories;

public interface IServiceRepository
{
    Task<IReadOnlyList<Service>> GetAllAsync(CancellationToken cancellationToken);

    Task<Service?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
