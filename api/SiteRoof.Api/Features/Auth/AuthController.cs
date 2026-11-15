using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Infrastructure;

namespace SiteRoof.Api.Features.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(200)] public string Password { get; set; } = "";
}

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(SessionService sessions, UserManager<AppUser> users, SignInManager<AppUser> signIn) : ControllerBase
{
    /// <summary>Current session. Also (re)issues the XSRF-TOKEN cookie used for unsafe requests.</summary>
    [HttpGet("me")]
    [AllowAnonymous]
    public Task<SessionDto> Me() => sessions.CurrentAsync();

    /// <summary>Password sign-in for named (non-demo) accounts. Demo personas have no password.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<SessionDto>> Login(LoginRequest req)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !await users.HasPasswordAsync(user))
        {
            return Problem(statusCode: 401, title: "Sign-in failed", detail: "Email or password is incorrect.");
        }
        var result = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return Problem(statusCode: 423, title: "Account locked", detail: "Too many failed attempts. Try again later.");
        }
        if (!result.Succeeded)
        {
            return Problem(statusCode: 401, title: "Sign-in failed", detail: "Email or password is incorrect.");
        }
        return await sessions.SignInAsync(user);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await sessions.SignOutAsync();
        return NoContent();
    }
}
