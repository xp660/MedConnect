using MediatR;
using MedConnect.Api.Common.Auth;
using MedConnect.Api.Contracts.Appointments;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedConnect.Api.Controllers;

[ApiController]
[Route("api/v1/appointments")]
[Authorize]
public sealed class AppointmentsController : ControllerBase
{
    private readonly ISender _sender;

    public AppointmentsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<IActionResult> Book([FromBody] BookAppointmentRequest request, CancellationToken cancellationToken)
    {
        var command = new BookAppointmentCommand(User.GetPatientId(), request.SlotId);
        var result = await _sender.Send(command, cancellationToken);

        var response = new BookAppointmentResponse(result.AppointmentId, result.ScheduleSlotId, result.PatientId, result.Status, result.BookedAtUtc);

        // 沒有用 CreatedAtAction：MVP 目前沒有「查詢單一 appointment」的 GET 端點可以讓它反查路由，
        // 硬用 nameof(Book) 只會在執行期丟 InvalidOperationException。Location 先用可預期的相對路徑組出來。
        return Created($"/api/v1/appointments/{response.AppointmentId}", response);
    }
}
