using FluentAssertions;
using MedConnect.Api.ExceptionHandling;
using MedConnect.Application.Common.Exceptions;
using MedConnect.Application.Common.Validation;
using MedConnect.Domain.Exceptions;
using MedConnect.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MedConnect.IntegrationTests.Logging;

/// <summary>
/// architecture-plan.md §0 v1.12 第 4 項的層級對照表，逐一釘住：GlobalExceptionHandler 是唯一決定例外
/// 日誌層級的地方，每個例外恰好一筆。直接呼叫 handler（不需要資料庫）。
/// </summary>
public class ExceptionLogLevelTests
{
    public static TheoryData<Exception, LogLevel, string> MappedExceptions => new()
    {
        { new ScheduleSlotNotFoundException(1), LogLevel.Information, "SLOT_NOT_FOUND" },
        { new DoctorNotFoundException(1), LogLevel.Information, "DOCTOR_NOT_FOUND" },
        { new AppointmentNotFoundException(1), LogLevel.Information, "APPOINTMENT_NOT_FOUND" },
        { new AppointmentAlreadyCancelledException(1), LogLevel.Information, "ALREADY_CANCELLED" },
        { new SlotFullException(1), LogLevel.Information, "SLOT_FULL" },
        { new SlotClosedException(1), LogLevel.Information, "SLOT_NOT_BOOKABLE" },
        { new SlotInPastException(1), LogLevel.Information, "SLOT_NOT_BOOKABLE" },
        { new DuplicateBookingException(1, 2, new Exception("unique index")), LogLevel.Information, "DUPLICATE_BOOKING" },
        { new InvalidCredentialsException(), LogLevel.Information, "INVALID_CREDENTIALS" },
        { new RequestValidationException([new ValidationError("slotId", "must be positive")]), LogLevel.Information, "VALIDATION_FAILED" },
        { new ConcurrencyConflictException(new Exception("version")), LogLevel.Information, "CONCURRENCY_CONFLICT" },
        { new TransientConflictException(new Exception("deadlock")), LogLevel.Warning, "TRANSIENT_CONFLICT" },
        { new RetryExhaustedException(4, new ConcurrencyConflictException(new Exception("version"))), LogLevel.Warning, "RETRY_EXHAUSTED" },
    };

    [Theory]
    [MemberData(nameof(MappedExceptions))]
    public async Task A_mapped_exception_is_logged_exactly_once_at_its_level_without_the_exception_object(
        Exception exception, LogLevel expectedLevel, string expectedErrorCode)
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var handled = await Handle(logger, exception);

        handled.Should().BeTrue();
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(expectedLevel);
        entry.Exception.Should().BeNull("已對映的例外是預期內事件，不帶例外物件、沒有堆疊");
        entry.State["ErrorCode"].Should().Be(expectedErrorCode);
    }

    [Fact]
    public async Task An_unmapped_exception_is_logged_exactly_once_as_Error_with_the_exception_and_left_unhandled()
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var bug = new InvalidOperationException("boom");

        var handled = await Handle(logger, bug);

        handled.Should().BeFalse("交還給預設的例外處理，回 500");
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeSameAs(bug);
    }

    private static ValueTask<bool> Handle(ILogger<GlobalExceptionHandler> logger, Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        return new GlobalExceptionHandler(logger).TryHandleAsync(context, exception, CancellationToken.None);
    }
}
