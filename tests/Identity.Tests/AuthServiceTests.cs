using FluentAssertions;
using Healthcare.Modules.Identity.Application;
using Healthcare.Modules.Identity.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Authentication is a §39 high-risk area: these are not optional.
/// The cases below are the ones where a plausible-looking implementation
/// quietly creates a vulnerability.
/// </summary>
namespace Healthcare.Tests.Identity;

public sealed class AuthServiceTests
{
    public AuthServiceTests()
    {
        _tokenService = new FakeTokenService(_tokens, _unitOfWork, _clock);
    }
    private readonly FakeUserRepository _users = new();
    private readonly InMemoryPasswordHasher _hasher = new();
    private readonly FakeRefreshTokenStore _tokens = new();
    private readonly RecordingSecurityEventRecorder _events = new();
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly FakeTokenService _tokenService;
    private readonly RecordingUnitOfWork _unitOfWork = new();

    private AuthService CreateSut() => new(
        _users,
        _hasher,
        _tokenService,
        _tokens,
        _events,
        _unitOfWork,
        _clock,
        NullLogger<AuthService>.Instance);

    private User SeedActiveUser(string email = "user@example.com")
    {
        var user = User.Create(email, _hasher.Hash("Str0ngPassword!"), _clock.GetUtcNow());
        user.AddRole(Roles.Customer);
        _users.Seed(user);

        return user;
    }

    [Fact]
    public async Task Register_gives_new_user_the_customer_role()
    {
        var result = await CreateSut().RegisterAsync(
            new RegisterRequest("New.User@Example.com", "Str0ngPassword!"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();

        var user = _users.FindByEmail("new.user@example.com");
        user.Should().NotBeNull();

        // Email is normalised on write, so the unique index needs no
        // functional form to catch case variants.
        user!.Roles.Should().ContainSingle(r => r.Role == Roles.Customer);
    }

    [Fact]
    public async Task Register_commits_to_the_database()
    {
        // This test exists because of a real bug. AddAsync stages the entity,
        // nothing called SaveChanges, the endpoint returned 201 Created, and
        // the users table stayed empty. Unit tests passed throughout because
        // the in-memory fake never needed a commit.
        //
        // A write that is not committed is indistinguishable from a write
        // that succeeds — until someone tries to log in and the user is not
        // there.
        await CreateSut().RegisterAsync(
            new RegisterRequest("commit@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        _unitOfWork.CommitCount.Should().Be(1, "registration must persist the user");
    }

    [Fact]
    public async Task Login_commits_the_refresh_token_and_the_login_timestamp()
    {
        SeedActiveUser();

        await CreateSut().AuthenticateAsync(
            new LoginRequest("user@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        _unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Refreshing_commits_the_rotation()
    {
        var user = SeedActiveUser();
        var sut = CreateSut();

        var first = await sut.AuthenticateAsync(
            new LoginRequest("user@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        var beforeRefresh = _unitOfWork.CommitCount;

        await sut.RefreshAsync(first.RefreshToken!, CancellationToken.None);

        // Without this commit the spent token stays valid, so a replay would
        // never be detected and rotation would be decorative.
        _unitOfWork.CommitCount.Should().BeGreaterThan(beforeRefresh);
    }

    [Fact]
    public async Task Change_password_commits_the_new_hash_and_revocations_together()
    {
        var user = SeedActiveUser();
        var sut = CreateSut();

        var result = await sut.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("Str0ngPassword!", "N3wStr0ngPassword!"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();

        // One commit, not two: a crash between the new hash and the token
        // revocations would leave the old password valid alongside fresh
        // sessions.
        _unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Logout_commits_the_revocation()
    {
        var user = SeedActiveUser();
        var sut = CreateSut();

        await sut.LogoutAsync(user.Id, CancellationToken.None);

        _unitOfWork.CommitCount.Should().Be(1, "logout must actually revoke tokens");
    }

    [Fact]
    public async Task A_failed_login_writes_nothing()
    {
        SeedActiveUser();

        await CreateSut().AuthenticateAsync(
            new LoginRequest("user@example.com", "wrong"),
            CancellationToken.None);

        _unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Register_rejects_duplicate_email_regardless_of_case()
    {
        SeedActiveUser("Taken@Example.com");

        var result = await CreateSut().RegisterAsync(
            new RegisterRequest("TAKEN@example.COM", "Str0ngPassword!"),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("email_taken");
    }

    [Fact]
    public async Task Every_failed_login_looks_identical_from_outside()
    {
        var active = SeedActiveUser("active@example.com");

        var disabled = SeedActiveUser("disabled@example.com");
        disabled.Deactivate(_clock.GetUtcNow());

        var sut = CreateSut();

        var unknownEmail = await sut.AuthenticateAsync(
            new LoginRequest("nobody@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        var wrongPassword = await sut.AuthenticateAsync(
            new LoginRequest("active@example.com", "wrong"),
            CancellationToken.None);

        var deactivated = await sut.AuthenticateAsync(
            new LoginRequest("disabled@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        // All three must be byte-identical to the client. A distinct
        // "account_inactive" would confirm that the address is registered,
        // which is what a credential-stuffing list is built from. The real
        // reason goes to the audit log instead.
        foreach (var result in new[] { unknownEmail, wrongPassword, deactivated })
        {
            result.Succeeded.Should().BeFalse();
            result.ErrorCode.Should().Be("invalid_credentials");
            result.ErrorMessage.Should().Be(
                "ایمیل یا رمز عبور نادرست است.",
                "the response must not reveal why the attempt failed");
        }

        // ...while the server-side record still distinguishes them.
        _events.Events.Should().Contain(e => e.EventType == "login.failed_unknown_email");
        _events.Events.Should().Contain(e => e.EventType == "login.failed_credentials");
        _events.Events.Should().Contain(e => e.EventType == "login.failed_inactive");
    }

    [Fact]
    public async Task Login_refuses_a_deactivated_account()
    {
        var user = SeedActiveUser();
        user.Deactivate(_clock.GetUtcNow());

        var result = await CreateSut().AuthenticateAsync(
            new LoginRequest("user@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        // Indistinguishable from a wrong password by design — see the
        // enumeration test above. What matters is that no token is issued.
        result.Succeeded.Should().BeFalse();
        result.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task Login_records_a_security_event_for_every_attempt()
    {
        SeedActiveUser();
        var sut = CreateSut();

        await sut.AuthenticateAsync(
            new LoginRequest("user@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        _events.Events.Should().Contain(e => e.EventType == "login.success" && e.Succeeded);

        await sut.AuthenticateAsync(
            new LoginRequest("user@example.com", "wrong"),
            CancellationToken.None);

        _events.Events.Should().Contain(e => e.EventType == "login.failed_credentials" && !e.Succeeded);
    }

    [Fact]
    public async Task Successful_login_stores_only_a_hash_of_the_refresh_token()
    {
        SeedActiveUser();

        var result = await CreateSut().AuthenticateAsync(
            new LoginRequest("user@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();

        // The raw token the client receives must not be what the server keeps.
        // Awaited rather than blocked on: a sync wait over async deadlocks
        // under the test runner's synchronization context.
        var raw = result.RefreshToken!;

        var lookupByRaw = await _tokens.FindAsync(raw, CancellationToken.None);
        lookupByRaw.Should().BeNull("the raw token must never be stored");

        var lookupByHash = await _tokens.FindAsync(AuthService.HashToken(raw), CancellationToken.None);
        lookupByHash.Should().NotBeNull("the hash must be what is stored");
    }

    [Fact]
    public async Task Replaying_a_used_refresh_token_revokes_every_token_for_that_user()
    {
        var user = SeedActiveUser();
        var sut = CreateSut();

        var first = await sut.AuthenticateAsync(
            new LoginRequest("user@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        // Legitimate rotation: use the token once.
        var rotated = await sut.RefreshAsync(first.RefreshToken!, CancellationToken.None);
        rotated.Succeeded.Should().BeTrue();

        // Replay it. This is the signal that the token leaked.
        var replayed = await sut.RefreshAsync(first.RefreshToken!, CancellationToken.None);

        replayed.Succeeded.Should().BeFalse();
        _tokens.RevokeAllCalls.Should().Contain(user.Id.ToString());
    }

    [Fact]
    public async Task Change_password_wrong_current_password_is_rejected()
    {
        var user = SeedActiveUser();

        var result = await CreateSut().ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("wrong", "N3wStr0ngPassword!"),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Change_password_revokes_existing_sessions()
    {
        var user = SeedActiveUser();
        var sut = CreateSut();

        var result = await sut.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("Str0ngPassword!", "N3wStr0ngPassword!"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();

        // Otherwise a stolen refresh token outlives the password change that
        // was meant to lock the attacker out.
        _tokens.RevokeAllCalls.Should().Contain(user.Id.ToString());
    }

    [Fact]
    public async Task Deactivated_user_cannot_use_a_refresh_token_and_loses_the_session()
    {
        var user = SeedActiveUser();
        var sut = CreateSut();

        var login = await sut.AuthenticateAsync(
            new LoginRequest("user@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        user.Deactivate(_clock.GetUtcNow());

        var refreshed = await sut.RefreshAsync(login.RefreshToken!, CancellationToken.None);

        refreshed.Succeeded.Should().BeFalse();

        // Refusing the call is not enough. A token for a disabled account
        // must not stay usable, so the whole family is cut.
        _tokens.RevokeAllCalls.Should().Contain(user.Id.ToString());
    }

    [Fact]
    public async Task Refreshing_does_not_rewrite_the_last_login_timestamp()
    {
        var user = SeedActiveUser();
        var sut = CreateSut();

        await sut.AuthenticateAsync(
            new LoginRequest("user@example.com", "Str0ngPassword!"),
            CancellationToken.None);

        var afterLogin = user.LastLoginAt;

        var refreshed = await sut.RefreshAsync(
            "refresh-" + user.Id + "-1",
            CancellationToken.None);

        refreshed.Succeeded.Should().BeTrue();
        user.LastLoginAt.Should().Be(afterLogin);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_change_its_password()
    {
        var user = SeedActiveUser();
        user.Deactivate(_clock.GetUtcNow());

        var result = await CreateSut().ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("Str0ngPassword!", "N3wStr0ngPassword!"),
            CancellationToken.None);

        // Otherwise a disabled account still has a working credential and
        // can churn its password while it sits deactivated.
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void User_cannot_hold_the_same_role_twice()
    {
        var user = SeedActiveUser();

        user.AddRole(Roles.Customer);

        user.Roles.Count(r => r.Role == Roles.Customer).Should().Be(1);
    }

    [Fact]
    public void Deactivation_preserves_the_account_rather_than_deleting_it()
    {
        // Prescriptions and audit rows reference users. Deleting the user
        // would either break those references or cascade away history that
        // §10 requires us to keep.
        var user = SeedActiveUser();
        user.AddRole(Roles.Doctor);

        user.Deactivate(_clock.GetUtcNow());

        user.IsActive.Should().BeFalse();
        user.DeactivatedAt.Should().NotBeNull();
        user.Roles.Should().Contain(r => r.Role == Roles.Doctor);
    }
}
