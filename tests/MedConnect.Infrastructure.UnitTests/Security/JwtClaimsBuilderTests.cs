using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using MedConnect.Application.Common.Auth;
using MedConnect.Domain.Enums;
using MedConnect.Infrastructure.Security;

namespace MedConnect.Infrastructure.UnitTests.Security;

public class JwtClaimsBuilderTests
{
    [Fact]
    public void BuildClaims_ProducesExactlyTheClaimsArchitecturePlanSection71Requires()
    {
        var claims = JwtClaimsBuilder.BuildClaims(userId: 11, patientId: 22, UserRole.Patient);

        claims.Select(c => c.Type).Should().BeEquivalentTo(new[]
        {
            JwtRegisteredClaimNames.Sub,
            JwtClaimNames.PatientId,
            ClaimTypes.Role,
            JwtRegisteredClaimNames.Jti,
        }, "每種 claim 恰好一個，不多不少");
        claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value.Should().Be("11");
        claims.Single(c => c.Type == JwtClaimNames.PatientId).Value.Should().Be("22");
        claims.Single(c => c.Type == ClaimTypes.Role).Value.Should().Be("Patient");
    }

    [Fact]
    public void BuildClaims_UsesTheRoleItWasGiven()
    {
        JwtClaimsBuilder.BuildClaims(1, 1, UserRole.Admin)
            .Single(c => c.Type == ClaimTypes.Role).Value.Should().Be("Admin");
    }

    [Fact]
    public void BuildClaims_GeneratesAFreshJtiEveryTime()
    {
        var first = JwtClaimsBuilder.BuildClaims(1, 1, UserRole.Patient).Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var second = JwtClaimsBuilder.BuildClaims(1, 1, UserRole.Patient).Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        first.Should().NotBe(second);
        Guid.TryParse(first, out _).Should().BeTrue();
    }
}
