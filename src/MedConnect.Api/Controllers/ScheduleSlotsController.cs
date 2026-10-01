using MediatR;
using MedConnect.Application.ScheduleSlots.Queries.GetAvailableSlots;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedConnect.Api.Controllers;

[ApiController]
[Route("api/v1/schedule-slots")]
[AllowAnonymous]
public sealed class ScheduleSlotsController : ControllerBase
{
    private readonly ISender _sender;

    public ScheduleSlotsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    public async Task<IActionResult> GetAvailableSlots([FromQuery] long doctorId, [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetAvailableSlotsQuery(doctorId, date), cancellationToken);
        return Ok(result);
    }
}
