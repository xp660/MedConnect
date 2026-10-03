using FluentAssertions;
using MediatR;
using MedConnect.Application.Appointments.Commands.BookAppointment;
using MedConnect.Application.Appointments.Commands.CancelAppointment;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Domain.Exceptions;
using MedConnect.Domain.ValueObjects;
using MedConnect.Infrastructure.Persistence;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit.Abstractions;

namespace MedConnect.IntegrationTests.Appointments;

/// <summary>
/// Cancel 與 Booking 一樣一次寫兩張表（schedule_slots.booked_count--、appointments.status = Cancelled），
/// 鎖定順序必須跟 Booking 一致（先 schedule_slots 再 appointments），否則 Book/Cancel 混跑會重新引入 1c 修掉的死結。
/// 這個類別證明的是：順序真的是那樣（用 MySQL server 端的 general log 看實際收到的語句，
/// 不是看 EF 自己印的）、兩次寫入真的在同一筆交易裡（用 trigger 強制第二次 UPDATE 失敗）、
/// 以及併發重複取消不會重複釋放名額。
/// </summary>
[Collection(MySqlContainerCollection.Name)]
public class CancelAppointmentTests
{
    // MySqlContainerFixture 用 .WithPassword("medconnect")，Testcontainers 會把同一組密碼也設成 root 密碼。
    // 建 trigger 與開 general log 需要 root 權限（binlog 開啟時建 trigger 要 SUPER）。
    private const string RootPassword = "medconnect";

    // SIGNAL SQLSTATE '45000' 的 MySQL error number（ER_SIGNAL_EXCEPTION）。
    private const int SignalExceptionErrorNumber = 1644;

    private readonly MySqlContainerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CancelAppointmentTests(MySqlContainerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Book_then_cancel_decrements_booked_count_and_cancels_appointment()
    {
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 3, patientCount: 2);
        var firstAppointmentId = await BookAsync(patientIds[0], slotId);
        await BookAsync(patientIds[1], slotId);

        var before = await ReadStateAsync(slotId, firstAppointmentId);
        before.Slot.BookedCount.Should().Be(2);

        var result = await CancelAsync(firstAppointmentId, patientIds[0]);

        var after = await ReadStateAsync(slotId, firstAppointmentId);
        _output.WriteLine($"slot booked_count {before.Slot.BookedCount} -> {after.Slot.BookedCount}, version {before.Slot.Version} -> {after.Slot.Version}");
        _output.WriteLine($"appointment status {before.Appointment.Status} -> {after.Appointment.Status}, version {before.Appointment.Version} -> {after.Appointment.Version}");

        result.Status.Should().Be(AppointmentStatus.Cancelled);
        after.Slot.BookedCount.Should().Be(1, "取消一筆，名額必須剛好釋放一個");
        after.Slot.Version.Should().Be(before.Slot.Version + 1);
        after.Appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        after.Appointment.CancelledAtUtc.Should().NotBeNull();
        after.Appointment.Version.Should().Be(before.Appointment.Version + 1);
        after.ActiveAppointments.Should().Be(after.Slot.BookedCount, "兩表必須一致，不能有 drift");
    }

    [Fact]
    public async Task After_cancel_the_same_patient_can_book_the_same_slot_again()
    {
        // 取消後 active_patient_id（generated column）會變成 NULL，才不會再被 ux_appt_slot_active_patient 擋住。
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 1, patientCount: 1);
        var appointmentId = await BookAsync(patientIds[0], slotId);
        await CancelAsync(appointmentId, patientIds[0]);

        var act = async () => await BookAsync(patientIds[0], slotId);

        await act.Should().NotThrowAsync();
        (await ReadStateAsync(slotId, appointmentId)).Slot.BookedCount.Should().Be(1);
    }

    [Fact]
    public async Task Cancel_updates_schedule_slots_before_appointments_inside_one_transaction()
    {
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 3, patientCount: 1);
        var appointmentId = await BookAsync(patientIds[0], slotId);

        await using var root = await OpenRootConnectionAsync();
        await ExecuteAsync(root, "SET GLOBAL log_output = 'TABLE'");
        await ExecuteAsync(root, "TRUNCATE TABLE mysql.general_log");
        await ExecuteAsync(root, "SET GLOBAL general_log = 'ON'");

        List<string> statements;
        try
        {
            await CancelAsync(appointmentId, patientIds[0]);

            statements = await ReadTransactionalStatementsAsync(root);
        }
        finally
        {
            await ExecuteAsync(root, "SET GLOBAL general_log = 'OFF'");
        }

        _output.WriteLine("MySQL general_log, statements the server received for ONE cancel:");
        for (var i = 0; i < statements.Count; i++)
        {
            _output.WriteLine($"  {i + 1}. {Shorten(statements[i])}");
        }

        statements.Should().HaveCount(4);
        statements[0].Should().StartWithEquivalentOf("START TRANSACTION");
        statements[1].Should().StartWithEquivalentOf("UPDATE `schedule_slots`", "slot 必須先被 UPDATE（先拿 X 鎖），跟 Booking 的鎖定順序一致");
        statements[2].Should().StartWithEquivalentOf("UPDATE `appointments`");
        statements[3].Should().StartWithEquivalentOf("COMMIT", "兩個 UPDATE 必須落在同一筆交易裡");
    }

    [Fact]
    public async Task If_the_appointment_update_fails_after_the_slot_update_succeeded_the_whole_transaction_rolls_back()
    {
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 3, patientCount: 1);
        var appointmentId = await BookAsync(patientIds[0], slotId);
        var before = await ReadStateAsync(slotId, appointmentId);

        // Fault injection：在 appointments 上裝一個只對這一筆預約、且只在「改成 Cancelled」時才觸發的 trigger，
        // 讓 Cancel 的第二次 UPDATE 必然失敗。第一次 UPDATE（schedule_slots）不受影響、會先成功——
        // 正好重現「第一次 SaveChanges 已成功、第二次才失敗」的危險視窗（跟 BookingTransactionRollbackTests 同一類驗證）。
        var triggerName = $"trg_fail_cancel_{appointmentId}";
        await using var root = await OpenRootConnectionAsync();
        await ExecuteAsync(root, $@"
            CREATE TRIGGER {triggerName} BEFORE UPDATE ON appointments FOR EACH ROW
            BEGIN
                IF NEW.id = {appointmentId} AND NEW.status = {(int)AppointmentStatus.Cancelled} THEN
                    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'forced failure for atomicity test';
                END IF;
            END");

        try
        {
            var act = async () => await CancelAsync(appointmentId, patientIds[0]);

            var thrown = await act.Should().ThrowAsync<Exception>();
            ContainsMySqlError(thrown.Which, SignalExceptionErrorNumber)
                .Should().BeTrue("失敗必須真的來自 trigger（SIGNAL），不是別的原因");
        }
        finally
        {
            await ExecuteAsync(root, $"DROP TRIGGER IF EXISTS {triggerName}");
        }

        var after = await ReadStateAsync(slotId, appointmentId);
        _output.WriteLine($"after failed cancel: slot booked_count={after.Slot.BookedCount} (was {before.Slot.BookedCount}), " +
                          $"slot version={after.Slot.Version} (was {before.Slot.Version}), appointment status={after.Appointment.Status}");

        after.Slot.BookedCount.Should().Be(before.Slot.BookedCount,
            "第二次 UPDATE 失敗時，已成功送出的第一次 UPDATE（booked_count--）必須跟著回滾");
        after.Slot.Version.Should().Be(before.Slot.Version);
        after.Appointment.Status.Should().Be(AppointmentStatus.Booked);
        after.Appointment.Version.Should().Be(before.Appointment.Version);
        after.ActiveAppointments.Should().Be(after.Slot.BookedCount, "兩表不可 drift");
    }

    [Fact]
    public async Task Cancelling_twice_throws_AlreadyCancelled_and_never_releases_the_slot_a_second_time()
    {
        // capacity 內只有這一筆預約：第一次取消後 booked_count 是 0。若 Handler 先 Release() 再檢查已取消，
        // 第二次取消會先撞上 SlotReleaseUnderflowException（未對映 → 500），而不是 409 ALREADY_CANCELLED。
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 3, patientCount: 1);
        var appointmentId = await BookAsync(patientIds[0], slotId);
        await CancelAsync(appointmentId, patientIds[0]);
        var afterFirst = await ReadStateAsync(slotId, appointmentId);
        afterFirst.Slot.BookedCount.Should().Be(0);

        var act = async () => await CancelAsync(appointmentId, patientIds[0]);

        await act.Should().ThrowExactlyAsync<AppointmentAlreadyCancelledException>();
        var afterSecond = await ReadStateAsync(slotId, appointmentId);
        afterSecond.Slot.BookedCount.Should().Be(0, "不可變負、不可 double-release");
        afterSecond.Slot.Version.Should().Be(afterFirst.Slot.Version, "被拒絕的請求不該動到任何一列");
    }

    [Fact]
    public async Task Cancelling_someone_elses_or_a_nonexistent_appointment_is_indistinguishable_and_changes_nothing()
    {
        var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 3, patientCount: 2);
        var appointmentId = await BookAsync(patientIds[0], slotId);
        var before = await ReadStateAsync(slotId, appointmentId);
        const long nonexistentId = 999_999_999;

        var notYours = async () => await CancelAsync(appointmentId, patientIds[1]);
        var missing = async () => await CancelAsync(nonexistentId, patientIds[1]);

        var notYoursEx = (await notYours.Should().ThrowExactlyAsync<AppointmentNotFoundException>()).Which;
        var missingEx = (await missing.Should().ThrowExactlyAsync<AppointmentNotFoundException>()).Which;
        notYoursEx.Message.Should().Be($"Appointment {appointmentId} was not found.");
        missingEx.Message.Should().Be($"Appointment {nonexistentId} was not found.");

        var after = await ReadStateAsync(slotId, appointmentId);
        after.Slot.BookedCount.Should().Be(before.Slot.BookedCount);
        after.Appointment.Status.Should().Be(AppointmentStatus.Booked);
    }

    [Fact]
    public async Task Concurrent_cancels_of_the_same_appointment_succeed_exactly_once_and_release_the_slot_exactly_once()
    {
        const int rounds = 5;
        const int concurrentCancels = 10;

        for (var round = 1; round <= rounds; round++)
        {
            // 3 個病人各訂一筆（booked_count = 3），只取消其中一筆：
            // 若發生 double-release，booked_count 會變成 1（甚至更低），單獨一筆預約的情境看不出來。
            var (slotId, patientIds) = await SeedSlotAndPatientsAsync(capacity: 5, patientCount: 3);
            var targetAppointmentId = await BookAsync(patientIds[0], slotId);
            await BookAsync(patientIds[1], slotId);
            await BookAsync(patientIds[2], slotId);

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempts = Enumerable.Range(0, concurrentCancels)
                .Select(_ => Task.Run(async () =>
                {
                    await gate.Task;

                    // 每個模擬請求自己一個 scope（跟 ConcurrentBookingTests 同理：共用 DbContext 測到的是 EF 記憶體行為）。
                    try
                    {
                        await CancelAsync(targetAppointmentId, patientIds[0]);
                        return "SUCCESS";
                    }
                    // 只接預期內的兩種失敗；死結（TransientConflictException）或任何其他例外都故意不接，
                    // 讓它往外拋、讓測試紅燈——那代表鎖定順序又壞了。
                    catch (AppointmentAlreadyCancelledException)
                    {
                        return "ALREADY_CANCELLED";
                    }
                    catch (ConcurrencyConflictException)
                    {
                        return "CONCURRENCY_CONFLICT";
                    }
                }))
                .ToArray();

            gate.SetResult();
            var outcomes = await Task.WhenAll(attempts);

            var summary = string.Join(", ", outcomes.GroupBy(o => o).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
            var state = await ReadStateAsync(slotId, targetAppointmentId);
            _output.WriteLine($"round {round}: {summary}; final booked_count={state.Slot.BookedCount} active_appointments={state.ActiveAppointments}");

            outcomes.Count(o => o == "SUCCESS").Should().Be(1, $"round {round}: 同一筆預約只能被成功取消一次");
            state.Slot.BookedCount.Should().Be(2, $"round {round}: 3 筆預約取消 1 筆，名額只能釋放一次（不可 double-release）");
            state.ActiveAppointments.Should().Be(state.Slot.BookedCount, $"round {round}: 兩表不可 drift");
            state.Appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        }
    }

    // ---------- helpers ----------

    private async Task<long> BookAsync(long patientId, long slotId)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new BookAppointmentCommand(patientId, slotId));
        return result.AppointmentId;
    }

    private async Task<CancelAppointmentResult> CancelAsync(long appointmentId, long patientId)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new CancelAppointmentCommand(appointmentId, patientId));
    }

    private async Task<(ScheduleSlot Slot, Appointment Appointment, int ActiveAppointments)> ReadStateAsync(long slotId, long appointmentId)
    {
        // 全新的 scope / DbContext / 連線重讀：MySQL 預設 REPEATABLE READ，沿用舊連線可能讀到舊快照。
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();

        var slot = await db.ScheduleSlots.AsNoTracking().SingleAsync(s => s.Id == slotId);
        var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId);
        var active = await db.Appointments.AsNoTracking()
            .CountAsync(a => a.SlotId == slotId && a.Status == AppointmentStatus.Booked);

        return (slot, appointment, active);
    }

    private async Task<(long SlotId, long[] PatientIds)> SeedSlotAndPatientsAsync(int capacity, int patientCount)
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var doctor = new Doctor("Cancel Test Doctor", "Cardiology", now);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        // 時段必須在未來：Booking 的 ScheduleSlot.Book() 對已開始的時段會丟 SlotInPastException。
        var start = now.UtcDateTime.AddDays(3);
        var slot = new ScheduleSlot(doctor.Id, new TimeSlot(start, start.AddMinutes(30)), capacity, now);
        db.ScheduleSlots.Add(slot);
        await db.SaveChangesAsync();

        var tag = Guid.NewGuid().ToString("N");
        var users = Enumerable.Range(1, patientCount)
            .Select(i => new User($"cancel-{tag}-{i}@cancel.test", "not-a-real-password-hash", UserRole.Patient, now))
            .ToArray();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var patients = users.Select((u, i) => new Patient(u.Id, $"Cancel Patient {i + 1}", now)).ToArray();
        db.Patients.AddRange(patients);
        await db.SaveChangesAsync();

        return (slot.Id, patients.Select(p => p.Id).ToArray());
    }

    private async Task<MySqlConnection> OpenRootConnectionAsync()
    {
        await using var scope = _fixture.ScopeFactory.CreateAsyncScope();
        var userConnectionString = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>().Database.GetConnectionString()
            ?? throw new InvalidOperationException("DbContext has no connection string.");

        var builder = new MySqlConnectionStringBuilder(userConnectionString) { UserID = "root", Password = RootPassword };
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 只挑「交易邊界 + 寫入」語句（排除 SELECT 與 root 連線自己的管理語句），保留 server 收到的順序。
    /// </summary>
    private static async Task<List<string>> ReadTransactionalStatementsAsync(MySqlConnection root)
    {
        await using var command = root.CreateCommand();
        // argument 是 BLOB，LIKE 預設區分大小寫；MySqlConnector 送的是小寫的 "start transaction" / "commit"，
        // 所以先轉成字串再用 LOWER() 比對，不能假設大小寫。
        command.CommandText =
            "SELECT CONVERT(argument USING utf8mb4) FROM mysql.general_log " +
            "WHERE command_type = 'Query' " +
            "AND (LOWER(CONVERT(argument USING utf8mb4)) LIKE 'start transaction%' " +
            "     OR LOWER(CONVERT(argument USING utf8mb4)) LIKE 'commit%' " +
            "     OR LOWER(CONVERT(argument USING utf8mb4)) LIKE 'rollback%' " +
            "     OR LOWER(CONVERT(argument USING utf8mb4)) LIKE 'update `%' " +
            "     OR LOWER(CONVERT(argument USING utf8mb4)) LIKE 'insert into `%') " +
            "ORDER BY event_time";

        var statements = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statements.Add(reader.GetString(0));
        }

        return statements;
    }

    private static bool ContainsMySqlError(Exception exception, int errorNumber)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException mySqlException && mySqlException.Number == errorNumber)
            {
                return true;
            }
        }

        return false;
    }

    private static string Shorten(string statement)
    {
        var singleLine = statement.Replace("\r", " ").Replace("\n", " ");
        return singleLine.Length <= 140 ? singleLine : singleLine[..140] + " ...";
    }
}
