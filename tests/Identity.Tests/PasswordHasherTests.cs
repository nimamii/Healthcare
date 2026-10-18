namespace Healthcare.Tests.Identity;

using FluentAssertions;
using Healthcare.Modules.Identity.Infrastructure;
using Xunit;

/// <summary>
/// The real hasher, tested.
///
/// The other auth tests use <see cref="InMemoryPasswordHasher"/> because
/// PBKDF2 at 210k iterations would add seconds to every one of them. That
/// left this class with no coverage at all: nothing had ever called Hash or
/// Verify until a running system rejected a correct password.
///
/// PBKDF2 is slow by design, so these use the real implementation and simply
/// tolerate the cost. It is the only way to know the format round-trips.
/// </summary>
public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_and_verify_round_trip()
    {
        var hash = _hasher.Hash("Str0ngPassw0rd!");

        _hasher.Verify("Str0ngPassw0rd!", hash).Should().BeTrue();
    }

    [Fact]
    public void Wrong_password_does_not_verify()
    {
        var hash = _hasher.Hash("Str0ngPassw0rd!");

        _hasher.Verify("str0ngpassw0rd!", hash).Should().BeFalse(
            "verification must be case sensitive");
    }

    [Fact]
    public void Similar_password_does_not_verify()
    {
        var hash = _hasher.Hash("Str0ngPassw0rd!");

        // One character different. A truncated or partial comparison would
        // pass this, which is why Verify uses a fixed-time compare.
        _hasher.Verify("Str0ngPassw0rd", hash).Should().BeFalse();
    }

    [Fact]
    public void Same_password_hashes_differently_each_time()
    {
        var first = _hasher.Hash("Str0ngPassw0rd!");
        var second = _hasher.Hash("Str0ngPassw0rd!");

        // Distinct salts. Identical hashes would mean two users with the same
        // password are visible as the same account in a database dump.
        first.Should().NotBe(second);
    }

    [Fact]
    public void Hash_format_carries_the_iteration_count()
    {
        var hash = _hasher.Hash("Str0ngPassw0rd!");

        // <iterations>.<salt>.<hash> — the count is stored so the cost can be
        // raised later without invalidating existing passwords.
        hash.Should().StartWith("210000.");

        var parts = hash.Split('.');
        parts.Should().HaveCount(3, "the separator must not appear in base64 output");
    }

    [Fact]
    public void Both_passwords_verify_against_their_own_hash()
    {
        // Guards the salt path specifically: if the salt were not round-tripped
        // through base64 correctly, Verify would derive a different key and
        // every login would fail.
        var first = _hasher.Hash("Str0ngPassw0rd!");
        var second = _hasher.Hash("Str0ngPassw0rd!");

        _hasher.Verify("Str0ngPassw0rd!", first).Should().BeTrue();
        _hasher.Verify("Str0ngPassw0rd!", second).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("210000.only-two-parts")]
    [InlineData("abc.salt.hash")]
    [InlineData("....")]
    public void Malformed_hash_fails_verification_rather_than_throwing(string stored)
    {
        // A corrupt row must not become a 500. It has to look like a failed
        // login, which is also the safe answer: it never authenticates anyone.
        _hasher.Verify("anything", stored).Should().BeFalse();
    }

    [Fact]
    public void Empty_password_still_hashes_and_verifies()
    {
        // Validation rejects short or empty passwords at the API boundary, but
        // the hasher must not throw if one ever reaches it.
        var hash = _hasher.Hash(string.Empty);

        _hasher.Verify(string.Empty, hash).Should().BeTrue();
    }

    [Fact]
    public void Non_ascii_password_round_trips()
    {
        // Persian text in a password is plausible for this product. A byte
        // length assumption or an encoding mismatch would break it here.
        const string password = "رمزعبورقوی۱۲۳";

        var hash = _hasher.Hash(password);

        _hasher.Verify(password, hash).Should().BeTrue();
    }

    [Fact]
    public void Long_password_round_trips()
    {
        // Passphrases can exceed any fixed buffer, so no length cap is assumed.
        var password = new string('a', 512);

        var hash = _hasher.Hash(password);

        _hasher.Verify(password, hash).Should().BeTrue();
    }
}