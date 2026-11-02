using BookingApi.Domain;

namespace BookingApi.Dtos;

public record BookingResponse(
    int Id,
    int ProviderId,
    string ProviderName,
    int ServiceId,
    string ServiceName,
    int CustomerId,
    string CustomerName,
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    DateTime CreatedAt,
    DateTime? CancelledAt)
{
    // 呼叫前必須已載入 Provider、Service、Customer 導覽屬性
    public static BookingResponse FromEntity(Booking booking) => new(
        booking.Id,
        booking.ProviderId,
        booking.Provider.Name,
        booking.ServiceId,
        booking.Service.Name,
        booking.CustomerId,
        booking.Customer.Name,
        booking.StartTime,
        booking.EndTime,
        booking.Status.ToString(),
        booking.CreatedAt,
        booking.CancelledAt);
}
