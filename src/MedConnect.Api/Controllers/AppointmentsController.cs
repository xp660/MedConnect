using MediatR;
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
        // "patient_id" 是 JwtTokenService（Infrastructure/Security/JwtTokenService.cs）簽發時
        // 用的字面 claim 名稱，兩邊必須手動保持一致——這裡故意不建一個只有兩處使用的共用常數
        // （architecture-plan.md §9.1：說不出解決什麼問題就不加）。
        // 通過 [Authorize] 驗證的 Token 理論上不可能缺這個 claim；如果真的缺了，
        // 這是預期外的異常，不吞成業務例外，讓它自然拋出。
        var patientIdClaim = User.FindFirst("patient_id")?.Value
            ?? throw new InvalidOperationException("Authenticated request is missing the required 'patient_id' claim.");
        var patientId = long.Parse(patientIdClaim);

        var command = new BookAppointmentCommand(patientId, request.SlotId);
        var result = await _sender.Send(command, cancellationToken);

        var response = new BookAppointmentResponse(result.AppointmentId, result.ScheduleSlotId, result.PatientId, result.Status, result.BookedAtUtc);

        // 沒有用 CreatedAtAction：MVP 目前沒有「查詢單一 appointment」的 GET 端點可以讓它反查路由，
        // 硬用 nameof(Book) 只會在執行期丟 InvalidOperationException。Location 先用可預期的相對路徑組出來。
        return Created($"/api/v1/appointments/{response.AppointmentId}", response);
    }
}
