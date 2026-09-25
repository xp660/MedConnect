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
}
