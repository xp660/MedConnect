using MediatR;
using MedConnect.Api.Contracts.Appointments;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using Microsoft.AspNetCore.Mvc;

namespace MedConnect.Api.Controllers;

[ApiController]
[Route("api/v1/appointments")]
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
        // 1c 還沒有 Login/JWT（1d 才做），正常設計下這個值應該從 ClaimsPrincipal 的 claim 讀出來，
        // 不是從這裡寫死。這裡暫時寫死成 DatabaseSeeder 建立的固定測試病人，1d 接上 JWT 後這一行
        // 會被「從 User 讀 patient_id claim」取代，Command/Handler/Repository 完全不需要跟著改。
        const long temporaryPatientId = 1;

        var command = new BookAppointmentCommand(temporaryPatientId, request.SlotId);
        var result = await _sender.Send(command, cancellationToken);

        var response = new BookAppointmentResponse(result.AppointmentId, result.ScheduleSlotId, result.PatientId, result.Status, result.BookedAtUtc);

        // 沒有用 CreatedAtAction：MVP 目前沒有「查詢單一 appointment」的 GET 端點可以讓它反查路由，
        // 硬用 nameof(Book) 只會在執行期丟 InvalidOperationException。Location 先用可預期的相對路徑組出來。
        return Created($"/api/v1/appointments/{response.AppointmentId}", response);
    }
}
