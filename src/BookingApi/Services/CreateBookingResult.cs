using BookingApi.Dtos;

namespace BookingApi.Services;

// IsReplay：這次回傳的是同一個 Idempotency-Key 第一次請求的結果，沒有建立新的預約
public record CreateBookingResult(BookingResponse Booking, bool IsReplay);
