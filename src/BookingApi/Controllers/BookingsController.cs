using BookingApi.Dtos;
using BookingApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingApi.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        CreateBookingRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _bookingService.CreateAsync(request, idempotencyKey, cancellationToken);

        if (result.IsReplay)
        {
            // 讓用戶端知道這是第一次請求的結果，不是新建立的預約
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        // 回傳 201，並在 Location 標頭放新資源的網址 /api/bookings/{id}
        return CreatedAtAction(nameof(GetByIdAsync), new { id = result.Booking.Id }, result.Booking);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return Ok(await _bookingService.GetByIdAsync(id, cancellationToken));
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAsync(int id, CancellationToken cancellationToken)
    {
        return Ok(await _bookingService.CancelAsync(id, cancellationToken));
    }
}
