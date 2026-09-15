namespace MedConnect.Api.Contracts.Appointments;

/// <summary>architecture-plan.md §7.3：request body 只有 slotId，不含 patientId。</summary>
public sealed record BookAppointmentRequest(long SlotId);
