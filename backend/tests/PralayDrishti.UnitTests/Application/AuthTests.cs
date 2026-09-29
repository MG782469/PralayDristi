using System.IdentityModel.Tokens.Jwt;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Options;
using PralayDrishti.Application.Common.Options;
using PralayDrishti.Application.Features.Commands.Auth.Login;
using PralayDrishti.Application.Features.Commands.Auth.Logout;
using PralayDrishti.Application.Features.Commands.Auth.RefreshToken;
using PralayDrishti.Application.Features.Commands.Auth.Register;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Infrastructure.Identity;
using PralayDrishti.UnitTests.Fakes;

namespace PralayDrishti.UnitTests.Application;

public class AuthTests
{
    private readonly FakeUserRepo _users = new();
    private readonly FakeTokenRepo _tokens = new();
    private readonly FakeUow _uow = new();
    private readonly FakeClock _clock = new();
    private readonly FakeCache _cache = new();
    private readonly JwtTokenService _jwt;
    private readonly AuthService _auth;

    public AuthTests()
    {
        _jwt = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "PralayDrishti", Audience = "PralayDrishti.Client", Secret = new string('k', 48),
            AccessTokenMinutes = 15, RefreshTokenDays = 7
        }), _clock);
        _auth = new AuthService(_tokens, _users, _uow, _jwt, _cache, _clock);
    }

    private RegisterCommandHandler Register(bool allow = true) =>
        new(_users, new FakeHasher(), _auth, _clock, Options.Create(new AuthOptions { AllowSelfRegistration = allow }));

    private LoginCommandHandler Login() => new(_users, new FakeHasher(), _auth, _clock);
    private RefreshTokenCommandHandler Refresh() => new(_auth);

    [Fact]
    public async Task Register_assigns_viewer_role()
    {
        var r = await Register().Handle(new RegisterCommand("new@x.in", "Secret123", "New User", null), default);
        Assert.Equal(UserRole.Viewer, r.User.Role);
        Assert.Single(_users.Items);
        Assert.Single(_tokens.Items);
    }

    [Fact]
    public async Task Register_rejected_when_self_registration_disabled()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Register(allow: false).Handle(new RegisterCommand("new@x.in", "Secret123", "N", null), default));
    }

    [Fact]
    public async Task Register_duplicate_email_conflicts()
    {
        await Register().Handle(new RegisterCommand("dup@x.in", "Secret123", "A", null), default);
        await Assert.ThrowsAsync<ConflictException>(() =>
            Register().Handle(new RegisterCommand("DUP@x.in ", "Secret123", "B", null), default));
    }

    [Fact]
    public async Task Login_with_wrong_password_fails()
    {
        await Register().Handle(new RegisterCommand("a@x.in", "Secret123", "A", null), default);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Login().Handle(new LoginCommand("a@x.in", "Wrong1234", null), default));
    }

    [Fact]
    public async Task Login_is_case_insensitive_on_email()
    {
        await Register().Handle(new RegisterCommand("Admin@X.com", "Secret123", "A", null), default);
        var r = await Login().Handle(new LoginCommand("  admin@x.COM ", "Secret123", null), default);
        Assert.Equal("Admin@X.com", r.User.Email);
    }

    [Fact]
    public async Task Refresh_rotates_token()
    {
        var first = await Register().Handle(new RegisterCommand("a@x.in", "Secret123", "A", null), default);
        var second = await Refresh().Handle(new RefreshTokenCommand(first.RefreshToken, null), default);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        var old = _tokens.Items.Single(t => t.TokenHash == _jwt.Hash(first.RefreshToken));
        Assert.True(old.IsRevoked);
        Assert.Equal(_jwt.Hash(second.RefreshToken), old.ReplacedByTokenHash);
    }

    [Fact]
    public async Task Reusing_revoked_token_revokes_all_user_tokens()
    {
        var first = await Register().Handle(new RegisterCommand("a@x.in", "Secret123", "A", null), default);
        await Refresh().Handle(new RefreshTokenCommand(first.RefreshToken, null), default);
        _clock.Advance(TimeSpan.FromMinutes(5));   // well outside the grace window

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Refresh().Handle(new RefreshTokenCommand(first.RefreshToken, null), default));
        Assert.All(_tokens.Items, t => Assert.True(t.IsRevoked));
    }

    [Fact]
    public async Task Refresh_within_grace_window_returns_same_successor()
    {
        var first = await Register().Handle(new RegisterCommand("a@x.in", "Secret123", "A", null), default);
        var tabA = await Refresh().Handle(new RefreshTokenCommand(first.RefreshToken, null), default);
        _clock.Advance(TimeSpan.FromSeconds(5));
        var tabB = await Refresh().Handle(new RefreshTokenCommand(first.RefreshToken, null), default);

        Assert.Equal(tabA.RefreshToken, tabB.RefreshToken);
        Assert.Contains(_tokens.Items, t => t.IsActive(_clock.UtcNow));
    }

    [Fact]
    public async Task Grace_entry_exists_before_rotation_is_committed()
    {
        // a second tab can read the rotated row the instant it commits; the successor must already be findable
        var first = await Register().Handle(new RegisterCommand("race@x.in", "Secret123", "R", null), default);
        bool graceVisibleAtCommit = false;
        _uow.OnSave = () => graceVisibleAtCommit = _cache.Keys.Any(k => k.StartsWith("rt-grace"));

        await Refresh().Handle(new RefreshTokenCommand(first.RefreshToken, null), default);

        Assert.True(graceVisibleAtCommit);
    }

    [Fact]
    public async Task Logout_revokes_token()
    {
        var first = await Register().Handle(new RegisterCommand("a@x.in", "Secret123", "A", null), default);
        await new LogoutCommandHandler(_auth).Handle(new LogoutCommand(first.RefreshToken, null), default);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Refresh().Handle(new RefreshTokenCommand(first.RefreshToken, null), default));
    }

    [Fact]
    public async Task Access_token_contains_user_id_email_role_claims()
    {
        var r = await Register().Handle(new RegisterCommand("claims@x.in", "Secret123", "C", null), default);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(r.AccessToken);
        Assert.Equal(r.User.Id.ToString(), jwt.Claims.Single(c => c.Type == "user_id").Value);
        Assert.Equal("claims@x.in", jwt.Claims.Single(c => c.Type == "email").Value);
        Assert.Equal("Viewer", jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal(_clock.UtcNow.AddMinutes(15), r.AccessTokenExpiresUtc, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("onlyletters")]
    [InlineData("12345678")]
    public void Password_needs_8_chars_letter_and_digit(string pwd)
    {
        var v = new RegisterCommandValidator();
        v.TestValidate(new RegisterCommand("a@x.in", pwd, "A", null)).ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Email_must_be_valid()
    {
        var v = new RegisterCommandValidator();
        v.TestValidate(new RegisterCommand("not-an-email", "Secret123", "A", null)).ShouldHaveValidationErrorFor(x => x.Email);
        v.TestValidate(new RegisterCommand("ok@x.in", "Secret123", "A", null)).ShouldNotHaveAnyValidationErrors();
    }
}
