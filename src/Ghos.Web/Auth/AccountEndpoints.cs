using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Ghos.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Auth;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/account")
            .RequireRateLimiting("authentication");

        group.MapPost("/login", LoginAsync);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapPost("/bootstrap", BootstrapAsync);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        [AsParameters] LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var returnUrl = GetSafeReturnUrl(request.ReturnUrl);

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.LocalRedirect(BuildLoginUrl(returnUrl, "required"));
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return Results.LocalRedirect(BuildLoginUrl(returnUrl, "invalid"));
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            request.RememberMe == true,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return Results.LocalRedirect(returnUrl);
        }

        return Results.LocalRedirect(BuildLoginUrl(
            returnUrl,
            result.IsLockedOut ? "locked" : "invalid"));
    }

    private static async Task<IResult> LogoutAsync(
        [AsParameters] LogoutRequest request,
        SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect(GetSafeReturnUrl(request.ReturnUrl, "/account/login"));
    }

    private static async Task<IResult> BootstrapAsync(
        [AsParameters] BootstrapRequest request,
        IConfiguration configuration,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext dbContext)
    {
        if (await dbContext.Users.AnyAsync())
        {
            return Results.LocalRedirect("/account/login?setup=complete");
        }

        var configuredToken = configuration["Bootstrap:Token"];
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return Results.LocalRedirect("/account/bootstrap?error=disabled");
        }

        if (!SecureEquals(configuredToken, request.SetupCode ?? string.Empty))
        {
            return Results.LocalRedirect("/account/bootstrap?error=code");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            request.Password != request.ConfirmPassword)
        {
            return Results.LocalRedirect("/account/bootstrap?error=validation");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return Results.LocalRedirect("/account/bootstrap?error=create");
        }

        var roleResult = await userManager.AddToRoleAsync(user, GhosRoles.Administrator);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return Results.LocalRedirect("/account/bootstrap?error=create");
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return Results.LocalRedirect("/");
    }

    private static bool SecureEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string GetSafeReturnUrl(string? returnUrl, string fallback = "/")
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !returnUrl.StartsWith('/') ||
            returnUrl.StartsWith("//") ||
            returnUrl.StartsWith("/\\"))
        {
            return fallback;
        }

        return returnUrl;
    }

    private static string BuildLoginUrl(string returnUrl, string error)
    {
        return $"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}&error={error}";
    }

    private sealed class LoginRequest
    {
        [FromForm]
        [EmailAddress]
        public string? Email { get; init; }

        [FromForm]
        public string? Password { get; init; }

        [FromForm]
        public bool? RememberMe { get; init; }

        [FromForm]
        public string? ReturnUrl { get; init; }
    }

    private sealed class LogoutRequest
    {
        [FromForm]
        public string? ReturnUrl { get; init; }
    }

    private sealed class BootstrapRequest
    {
        [FromForm]
        public string? SetupCode { get; init; }

        [FromForm]
        public string? DisplayName { get; init; }

        [FromForm]
        [EmailAddress]
        public string? Email { get; init; }

        [FromForm]
        public string? Password { get; init; }

        [FromForm]
        public string? ConfirmPassword { get; init; }
    }
}
