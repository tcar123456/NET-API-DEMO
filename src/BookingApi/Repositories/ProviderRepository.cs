using BookingApi.Data;
using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Repositories;

public class ProviderRepository : IProviderRepository
{
    private readonly AppDbContext _db;

    public ProviderRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Provider>> GetActiveAsync(CancellationToken cancellationToken)
    {
        return await _db.Providers
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Provider?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return _db.Providers
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }
}
