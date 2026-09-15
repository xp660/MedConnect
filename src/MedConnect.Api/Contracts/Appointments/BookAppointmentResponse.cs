using MedConnect.Domain.Enums;

namespace MedConnect.Api.Contracts.Appointments;

/// <summary>
/// architecture-plan.md §7.3 的 201 回應形狀。獨立於 Application 的 BookAppointmentResult，
/// 因為欄位命名要對齊已定案的公開 API 合約（slotId），而 Application 層的型別不該帶 JSON 序列化考量。
/// </summary>
public sealed record BookAppointmentResponse(
    long AppointmentId,
    long SlotId,
    long PatientId,
    AppointmentStatus Status,
    DateTime BookedAtUtc);
