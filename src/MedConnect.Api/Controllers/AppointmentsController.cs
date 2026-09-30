using MediatR;
using MedConnect.Api.Contracts.Appointments;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Application.Common.Auth;
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
        // 通過 [Authorize] 驗證的 Token 理論上不可能缺 JwtClaimNames.PatientId 這個 claim；
        // 如果真的缺了，這是預期外的異常，不吞成業務例外，讓它自然拋出。
        var patientIdClaim = User.FindFirst(JwtClaimNames.PatientId)?.Value
            ?? throw new InvalidOperationException($"Authenticated request is missing the required '{JwtClaimNames.PatientId}' claim.");
        var patientId = long.Parse(patientIdClaim);

        var command = new BookAppointmentCommand(patientId, request.SlotId);
        var result = await _sender.Send(command, cancellationToken);

        var response = new BookAppointmentResponse(result.AppointmentId, result.ScheduleSlotId, result.PatientId, result.Status, result.BookedAtUtc);

        // 沒有用 CreatedAtAction：MVP 目前沒有「查詢單一 appointment」的 GET 端點可以讓它反查路由，
        // 硬用 nameof(Book) 只會在執行期丟 InvalidOperationException。Location 先用可預期的相對路徑組出來。
        return Created($"/api/v1/appointments/{response.AppointmentId}", response);
    }
}
