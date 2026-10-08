using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace MedConnect.ArchitectureTests;

internal static class DependencyRuleAssertions
{
    /// <summary>
    /// 斷言 assembly 內沒有任何型別依賴任何一個 forbidden 命名空間。
    /// 失敗訊息會列出違規的具體型別全名——不能只說「違反了」，維護的人需要知道是哪個型別。
    /// </summary>
    public static void ShouldNotDependOn(Assembly assembly, params string[] forbiddenNamespaces)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        var violators = result.FailingTypeNames is { Count: > 0 }
            ? string.Join(Environment.NewLine + "  - ", result.FailingTypeNames)
            : "(none)";

        result.IsSuccessful.Should().BeTrue(
            "{0} 不可依賴 [{1}]。違規型別：{2}  - {3}",
            assembly.GetName().Name,
            string.Join(", ", forbiddenNamespaces),
            Environment.NewLine,
            violators);
    }

    /// <summary>正向對照：assembly 內是否至少有一個型別依賴指定命名空間。只用在驗證「掃描器本身有效」。</summary>
    public static bool HasAnyTypeDependingOn(Assembly assembly, string @namespace) =>
        Types.InAssembly(assembly).That().HaveDependencyOn(@namespace).GetTypes().Any();
}
