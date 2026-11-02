using BookingApi.Dtos;

namespace BookingApi.Services;

public interface IBookingService
{
    Task<IReadOnlyList<TimeSlotResponse>> GetAvailableSlotsAsync(
        int providerId, DateOnly date, CancellationToken cancellationToken);

    Task<BookingResponse> CreateAsync(CreateBookingRequest request, CancellationToken cancellationToken);

    // idempotencyKey 為 null 時等同上面的 CreateAsync；有值時，同一個 Key 重送會回傳第一次的結果
    Task<CreateBookingResult> CreateAsync(
        CreateBookingRequest request, string? idempotencyKey, CancellationToken cancellationToken);

    Task<BookingResponse> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<BookingResponse> CancelAsync(int id, CancellationToken cancellationToken);

    Task<PagedResponse<BookingResponse>> GetCustomerBookingsAsync(
        int customerId, int page, int pageSize, CancellationToken cancellationToken);
}
