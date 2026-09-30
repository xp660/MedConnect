using FluentAssertions;
using MedConnect.Infrastructure.Security;

namespace MedConnect.Infrastructure.UnitTests.Security;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_CalledTwiceWithSamePlainPassword_ProducesDifferentHashes()
    {
        // 每次 Hash 都要用新的 Salt，同樣明文兩次結果必須不同，否則等於沒有 Salt。
        var first = _hasher.Hash("Test1234!");
        var second = _hasher.Hash("Test1234!");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Verify_WithMatchingPlainPassword_ReturnsTrueRegardlessOfWhichHashWasProduced()
    {
        var first = _hasher.Hash("Test1234!");
        var second = _hasher.Hash("Test1234!");

        _hasher.Verify("Test1234!", first).Should().BeTrue();
        _hasher.Verify("Test1234!", second).Should().BeTrue();
    }

    [Fact]
    public void Verify_WithWrongPlainPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("Test1234!");

        _hasher.Verify("WrongPassword!", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_WithMalformedHash_ReturnsFalseInsteadOfThrowing()
    {
        // 回歸測試：資料庫裡存的雜湊值格式不合法（例如殘留的舊 placeholder 字串）時，
        // 底層 EnhancedVerify 會丟 SaltParseException。呼叫端不該收到未預期的例外，
        // 必須跟「密碼錯」回傳同一種結果（false），最終才會對外變成同一種 401。
        var act = () => _hasher.Verify("Test1234!", "not-a-real-password-hash");

        act.Should().NotThrow();
        act().Should().BeFalse();
    }
}
