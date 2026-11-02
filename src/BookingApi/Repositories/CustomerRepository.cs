using BookingApi.Data;
using BookingApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _db;

    public CustomerRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
}
