namespace MedConnect.Application.Common.Exceptions;

/// <summary>
/// 「預約不存在」與「預約存在但不屬於當前使用者」刻意共用這一個例外型別、同一個訊息
/// （architecture-plan.md §7.4）：呼叫端若能分辨這兩種情況，就能用 appointmentId 枚舉別人的預約。
/// 所以建構子只收 appointmentId，不帶任何能洩漏「是哪一種」的資訊。
/// </summary>
public sealed class AppointmentNotFoundException : Exception
{
    public long AppointmentId { get; }

    public AppointmentNotFoundException(long appointmentId)
        : base($"Appointment {appointmentId} was not found.")
    {
        AppointmentId = appointmentId;
    }
}
