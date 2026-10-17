using System.Collections.Concurrent;
using System.Net.Http.Headers;
using MedConnect.Api.Logging;
using MedConnect.Application.Abstractions;
using MedConnect.Domain.Entities;
using MedConnect.Domain.Enums;
using MedConnect.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace MedConnect.IntegrationTests.Infrastructure;

/// <summary>
/// 測試用的日誌捕捉 sink。它是透過 DI 註冊的 <see cref="ILogEventSink"/>，由 Program.cs 的
/// UseSerilog(... ReadFrom.Services(...)) 接進「正式的那一條 logger」，而過濾規則（ApiLogging.Configure 的
/// Filter.ByExcluding）設在 logger 根層級，所以這個 sink 收到的永遠是過濾「之後」的事件——
/// 也就是正式環境真的會輸出的內容。測試沒有自己另外掛一條平行的 logger。
/// </summary>
public sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public void Clear() => _events.Clear();
}

public static class LogEventExtensions
{
    public static string? SourceContext(this LogEvent logEvent) => logEvent.Text("SourceContext");

    public static string? Text(this LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    public static bool IsFrom(this LogEvent logEvent, Type sourceType) => logEvent.SourceContext() == sourceType.FullName;

    public static bool IsFromNamespace(this LogEvent logEvent, string namespacePrefix) =>
        logEvent.SourceContext()?.StartsWith(namespacePrefix, StringComparison.Ordinal) == true;

    /// <summary>訊息、所有屬性與例外攤平成一個字串，給「這段文字不可出現在任何日誌」的斷言用。</summary>
    public static string Everything(this LogEvent logEvent) =>
        string.Join(
            " | ",
            new[] { logEvent.RenderMessage(), logEvent.Exception?.ToString() ?? string.Empty }
                .Concat(logEvent.Properties.Select(p => $"{p.Key}={p.Value}")));
}

public static class LogCaptureExtensions
{
    /// <summary>
    /// 把 <paramref name="sink"/> 接進這個 factory 的 host。預設把最低層級降到 Verbose（並清掉 Microsoft.* 的覆寫），
    /// 讓需要「任何一行日誌都不可出現…」的測試看到所有層級；只想看正式環境預設輸出的測試傳 verbose: false。
    /// </summary>
    public static WebApplicationFactory<Program> WithLogCapture(
        this WebApplicationFactory<Program> factory,
        CapturingLogSink sink,
        bool verbose = true,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? extraConfiguration = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>();
                if (verbose)
                {
                    values["Serilog:MinimumLevel:Default"] = "Verbose";
                    values["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose";
                    values["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore"] = "Verbose";
                    // 事件照樣全部進捕捉 sink，只是 Console 不印，避免測試輸出被數百行 Debug 淹沒。
                    values[ApiLogging.ConsoleMinimumLevelKey] = "Warning";
                }

                foreach (var (key, value) in extraConfiguration ?? new Dictionary<string, string?>())
                {
                    values[key] = value;
                }

                config.AddInMemoryCollection(values);
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILogEventSink>(sink);
                configureServices?.Invoke(services);
            });
        });
}

public static class PatientClients
{
    /// <summary>建立一位新病人，回傳帶有效 JWT 的 HttpClient 與病人 Id。</summary>
    public static async Task<(HttpClient Client, long PatientId)> CreateAuthenticatedAsync(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var user = new User($"log-{Guid.NewGuid():N}@logging.test", "not-a-real-password-hash", UserRole.Patient, now);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var patient = new Patient(user.Id, "Logging Test Patient", now);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateToken(user, patient.Id);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        return (client, patient.Id);
    }

    /// <summary>直接用 SQL 之外最省事的方式：用 DbContext 建一個全新的、容量與狀態可控的時段。</summary>
    public static async Task<long> CreateSlotAsync(WebApplicationFactory<Program> factory, int capacity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedConnectDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

        var doctor = new Doctor($"Logging Doctor {Guid.NewGuid():N}", "Cardiology", now);
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        var start = now.UtcDateTime.AddDays(1);
        var slot = new ScheduleSlot(
            doctor.Id, new Domain.ValueObjects.TimeSlot(start, start.AddMinutes(30)), capacity, now);
        db.ScheduleSlots.Add(slot);
        await db.SaveChangesAsync();

        return slot.Id;
    }
}
