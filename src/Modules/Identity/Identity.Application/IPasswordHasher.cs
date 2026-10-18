namespace Healthcare.Modules.Identity.Application;

/// <summary>
/// One-way password hashing. An interface so tests can use a fast hasher and
/// so the algorithm can be upgraded later without touching call sites — old
/// hashes stay verifiable while new ones use a stronger cost factor.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
