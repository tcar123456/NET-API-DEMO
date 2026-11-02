using BookingApi.Dtos;
using BookingApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BookingApi.Controllers;

[ApiController]
[Route("api/providers")]
public class ProvidersController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly IBookingService _bookingService;

    public ProvidersController(ICatalogService catalogService, IBookingService bookingService)
    {
        _catalogService = catalogService;
        _bookingService = bookingService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProviderResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProvidersAsync(CancellationToken cancellationToken)
    {
        return Ok(await _catalogService.GetProvidersAsync(cancellationToken));
    }

    [HttpGet("{id:int}/available-slots")]
    [ProducesResponseType(typeof(IReadOnlyList<TimeSlotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailableSlotsAsync(
        int id, [FromQuery, BindRequired] DateOnly date, CancellationToken cancellationToken)
    {
        return Ok(await _bookingService.GetAvailableSlotsAsync(id, date, cancellationToken));
    }
}
