using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace MedConnect.ArchitectureTests;

/// <summary>
/// 驗證「掃描器本身有效」，解決的唯一問題：NetArchTest 對空集合或打錯的命名空間字串一律回報成功，
/// 規則可能因此永遠綠燈卻什麼都沒擋。
///
/// 兩種檢查：
///  1. 每個被掃描的組件裡真的有型別（不是掃到空組件）。
///  2. 正向對照——對「確實存在的依賴」，同一套命名空間字串必須找得到。Domain ← Application ←
///     Infrastructure ← Api 的依賴在專案層級是單向的，無法用「暫時加一行違規程式碼」去反向驗證
///     規則 2 和 4（那會變成循環參考、編譯不過），所以改用這裡的正向對照證明字串比對可用。
/// </summary>
public class ScannerSanityTests
{
    public static TheoryData<string, Assembly> ScannedAssemblies => new()
    {
        { "Domain", ArchitectureAssemblies.Domain },
        { "Application", ArchitectureAssemblies.Application },
        { "Infrastructure", ArchitectureAssemblies.Infrastructure },
        { "Api", ArchitectureAssemblies.Api },
    };

    [Theory]
    [MemberData(nameof(ScannedAssemblies))]
    public void Every_scanned_assembly_contains_types(string layer, Assembly assembly)
    {
        var typeCount = Types.InAssembly(assembly).GetTypes().Count();

        typeCount.Should().BeGreaterThan(0, "{0} 組件掃描到 0 個型別，所有針對它的規則都會空轉成功", layer);
    }

    [Theory]
    [InlineData("Application", Namespaces.Domain)]
    [InlineData("Infrastructure", Namespaces.Application)]
    [InlineData("Infrastructure", Namespaces.Domain)]
    [InlineData("Infrastructure", Namespaces.EfCore)]
    [InlineData("Infrastructure", Namespaces.MySqlConnector)]
    [InlineData("Application", Namespaces.MediatR)]
    [InlineData("Api", Namespaces.Application)]
    [InlineData("Api", Namespaces.Infrastructure)]
    [InlineData("Api", Namespaces.AspNetCore)]
    public void The_namespace_strings_used_by_the_rules_do_match_real_dependencies(string layer, string dependency)
    {
        var assembly = layer switch
        {
            "Application" => ArchitectureAssemblies.Application,
            "Infrastructure" => ArchitectureAssemblies.Infrastructure,
            "Api" => ArchitectureAssemblies.Api,
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };

        DependencyRuleAssertions.HasAnyTypeDependingOn(assembly, dependency)
            .Should().BeTrue("{0} 確實依賴 {1}；找不到代表命名空間字串打錯或掃描器沒在掃", layer, dependency);
    }
}
