namespace BookingApi.Dtos;

// StartTime 為台灣時間，格式不帶時區，例如 "2026-11-20T14:00:00"
public record CreateBookingRequest(int ProviderId, int ServiceId, int CustomerId, DateTime StartTime);
