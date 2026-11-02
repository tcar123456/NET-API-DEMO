using BookingApi.Dtos;

namespace BookingApi.Services;

public interface IBookingService
{
    Task<IReadOnlyList<TimeSlotResponse>> GetAvailableSlotsAsync(
        int providerId, DateOnly date, CancellationToken cancellationToken);

    Task<BookingResponse> CreateAsync(CreateBookingRequest request, CancellationToken cancellationToken);

    Task<BookingResponse> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<BookingResponse> CancelAsync(int id, CancellationToken cancellationToken);

    Task<PagedResponse<BookingResponse>> GetCustomerBookingsAsync(
        int customerId, int page, int pageSize, CancellationToken cancellationToken);
}
