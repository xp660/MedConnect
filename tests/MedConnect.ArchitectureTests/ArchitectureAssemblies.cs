using System.Reflection;

namespace MedConnect.ArchitectureTests;

/// <summary>
/// 被掃描的四個組件，以及規則裡會用到的命名空間字串。
///
/// 組件用各層裡一個確定存在的型別取得（不用 Assembly.Load 字串），這樣層的型別被改名或搬走時是
/// 編譯錯誤，而不是測試悄悄掃到別的東西。命名空間字串的有效性由 ScannerSanityTests 的正向對照驗證。
/// </summary>
internal static class ArchitectureAssemblies
{
    public static readonly Assembly Domain = typeof(MedConnect.Domain.Entities.ScheduleSlot).Assembly;
    public static readonly Assembly Application = typeof(MedConnect.Application.Abstractions.IUnitOfWork).Assembly;
    public static readonly Assembly Infrastructure = typeof(MedConnect.Infrastructure.Persistence.UnitOfWork).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;
}

internal static class Namespaces
{
    public const string Domain = "MedConnect.Domain";
    public const string Application = "MedConnect.Application";
    public const string Infrastructure = "MedConnect.Infrastructure";
    public const string Api = "MedConnect.Api";

    public const string EfCore = "Microsoft.EntityFrameworkCore";
    public const string MySqlConnector = "MySqlConnector";
    public const string MediatR = "MediatR";
    public const string AspNetCore = "Microsoft.AspNetCore";
}
