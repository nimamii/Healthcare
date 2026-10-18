using Healthcare.Modules.Identity.Application;
using Healthcare.Modules.Identity.Domain;

/// <summary>
/// Deterministic stand-in for the production hasher. PBKDF2 at 210k iterations
/// would add seconds to every test; what these tests exercise is the auth
/// flow's control logic, not the KDF.
/// </summary>
namespace Healthcare.Tests.Identity;

internal sealed class InMemoryPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "test." + password;

    public bool Verify(string password, string passwordHash) =>
        string.Equals(Hash(password), passwordHash, StringComparison.Ordinal);
}
