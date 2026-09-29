using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PralayDrishti.API.Extensions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Features.Commands.Auth.ChangePassword;
using PralayDrishti.Application.Features.Commands.Auth.Login;
using PralayDrishti.Application.Features.Commands.Auth.Logout;
using PralayDrishti.Application.Features.Commands.Auth.RefreshToken;
using PralayDrishti.Application.Features.Commands.Auth.Register;
using PralayDrishti.Application.Features.Queries.Auth.GetCurrentUser;

namespace PralayDrishti.API.Controllers;

[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private const string RefreshCookie = "pd_refresh";

    public sealed record RegisterRequest(string Email, string Password, string FullName);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record RefreshRequest(string? RefreshToken);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    /// <summary>Self-registration (when enabled). New accounts are Viewers.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResult>> Register(RegisterRequest r, CancellationToken ct) =>
        WithCookie(await Sender.Send(new RegisterCommand(r.Email, r.Password, r.FullName, ClientIp), ct));

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResult>> Login(LoginRequest r, CancellationToken ct) =>
        WithCookie(await Sender.Send(new LoginCommand(r.Email, r.Password, ClientIp), ct));

    /// <summary>Rotates the refresh token. Accepts it from the body (Swagger, tools) or the HttpOnly cookie (browser).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResult>> Refresh(RefreshRequest? r, CancellationToken ct)
    {
        string token = r?.RefreshToken ?? Request.Cookies[RefreshCookie] ?? "";
        return WithCookie(await Sender.Send(new RefreshTokenCommand(token, ClientIp), ct));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(RefreshRequest? r, CancellationToken ct)
    {
        await Sender.Send(new LogoutCommand(r?.RefreshToken ?? Request.Cookies[RefreshCookie], ClientIp), ct);
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/auth" });
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize(Policy = Policies.CanRead)]
    public Task<UserDto> Me(CancellationToken ct) => Sender.Send(new GetCurrentUserQuery(), ct);

    [HttpPost("change-password")]
    [Authorize(Policy = Policies.CanRead)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest r, CancellationToken ct)
    {
        await Sender.Send(new ChangePasswordCommand(r.CurrentPassword, r.NewPassword), ct);
        return NoContent();
    }

    private AuthResult WithCookie(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = result.RefreshTokenExpiresUtc
        });
        return result;
    }
}
