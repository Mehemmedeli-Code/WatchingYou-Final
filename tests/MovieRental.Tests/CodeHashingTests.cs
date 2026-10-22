using MovieRental.Modules.Cinema.Infrastructure;
using IdentityHasher = MovieRental.Modules.Identity.Infrastructure.VerificationCodeHasher;

namespace MovieRental.Tests;

/// <summary>
/// Both one-time codes — account verification and booking confirmation — are stored hashed.
/// A six-digit code is short enough that anyone reading the table could otherwise use it,
/// and these codes gate an account and a payment respectively.
/// </summary>
public class CodeHashingTests
{
    [Fact]
    public void A_code_verifies_against_its_own_hash()
    {
        var (hash, salt) = CodeHasher.Create("483920");
        Assert.True(CodeHasher.Verify("483920", hash, salt));
    }

    [Fact]
    public void A_wrong_code_does_not_verify()
    {
        var (hash, salt) = CodeHasher.Create("483920");
        Assert.False(CodeHasher.Verify("483921", hash, salt));
    }

    [Fact]
    public void The_hash_is_not_the_code()
    {
        var (hash, _) = CodeHasher.Create("000000");
        Assert.DoesNotContain("000000", hash);
    }

    [Fact]
    public void The_same_code_hashes_differently_every_time()
    {
        // Per-code salt. Without it, two users holding the same six digits would share a
        // hash, and one leaked pair would unlock every account that happened to match.
        var first = CodeHasher.Create("123456");
        var second = CodeHasher.Create("123456");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.True(CodeHasher.Verify("123456", first.Hash, first.Salt));
        Assert.True(CodeHasher.Verify("123456", second.Hash, second.Salt));
    }

    [Fact]
    public void A_hash_from_one_salt_does_not_verify_against_another()
    {
        var first = CodeHasher.Create("555000");
        var second = CodeHasher.Create("555000");
        Assert.False(CodeHasher.Verify("555000", first.Hash, second.Salt));
    }

    [Fact]
    public void Account_verification_uses_the_same_discipline()
    {
        var (hash, salt) = IdentityHasher.Create("777111");
        Assert.True(IdentityHasher.Verify("777111", hash, salt));
        Assert.False(IdentityHasher.Verify("777112", hash, salt));
        Assert.DoesNotContain("777111", hash);
    }
}
