using BookingApi.Dtos;
using BookingApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BookingApi.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("monthly-revenue")]
    [ProducesResponseType(typeof(MonthlyRevenueReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMonthlyRevenueAsync(
        [FromQuery, BindRequired] int year,
        [FromQuery, BindRequired] int month,
        CancellationToken cancellationToken)
    {
        return Ok(await _reportService.GetMonthlyRevenueAsync(year, month, cancellationToken));
    }
}
