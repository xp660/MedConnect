namespace MedConnect.ArchitectureTests;

/// <summary>
/// architecture-plan.md §4.2 的依賴方向（Domain ← Application ← Infrastructure ← Api）。
///
/// 專案之間的 ProjectReference 只能擋住「組件層級」的反向引用（循環參考根本編譯不過），
/// 擋不住的是：Domain 偷偷引用某個 NuGet 套件、Application 因為傳遞依賴而用到 EF Core 型別、
/// Infrastructure 的程式碼用了 Api 的命名空間。這些是這組測試真正在守的東西。
///
/// 規則一律寫成最嚴格版本，不留例外子句：若某條紅燈，先判斷是測試太嚴還是程式碼真的違規，
/// 與使用者討論後再決定，不是直接加例外讓它綠燈（CLAUDE.md §7）。
/// </summary>
public class DependencyDirectionTests
{
    [Fact]
    public void Domain_depends_on_no_other_layer_and_no_external_technology()
    {
        DependencyRuleAssertions.ShouldNotDependOn(
            ArchitectureAssemblies.Domain,
            Namespaces.Application,
            Namespaces.Infrastructure,
            Namespaces.Api,
            Namespaces.EfCore,
            Namespaces.MySqlConnector,
            Namespaces.MediatR,
            Namespaces.AspNetCore);
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_Api()
    {
        DependencyRuleAssertions.ShouldNotDependOn(
            ArchitectureAssemblies.Application,
            Namespaces.Infrastructure,
            Namespaces.Api);
    }

    /// <summary>
    /// 沒有任何例外子句：Query Repository 的模式本來就是「介面在 Application、實作在 Infrastructure」
    /// （§0 v1.6），Application 沒有任何型別應該需要 EF Core。實測確認過 Application 的原始碼與編譯後的
    /// 組件引用都沒有 EF Core，所以這裡直接寫嚴格版本。
    /// </summary>
    [Fact]
    public void Application_does_not_depend_on_EntityFrameworkCore_at_all()
    {
        DependencyRuleAssertions.ShouldNotDependOn(
            ArchitectureAssemblies.Application,
            Namespaces.EfCore);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Api()
    {
        DependencyRuleAssertions.ShouldNotDependOn(
            ArchitectureAssemblies.Infrastructure,
            Namespaces.Api);
    }
}
