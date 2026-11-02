using BookingApi.Data;
using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Repositories;

public class ServiceRepository : IServiceRepository
{
    private readonly AppDbContext _db;

    public ServiceRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Service>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Services
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Service?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return _db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }
}
