using System.ComponentModel.DataAnnotations;
using Ghos.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Auth;

public sealed record ManagedUser(
    string Id,
    string DisplayName,
    string Email,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Roles);

public sealed class UserAdministrationException : Exception
{
    public UserAdministrationException(string message)
        : base(message)
    {
    }
}

public sealed class UserAdministrationService(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<UserAdministrationService> logger)
{
    public async Task<IReadOnlyList<ManagedUser>> ListAsync()
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync();
        var users = await dbContext.Users
            .AsNoTracking()
            .OrderByDescending(user => user.IsActive)
            .ThenBy(user => user.DisplayName)
            .ToListAsync();
        var result = new List<ManagedUser>(users.Count);
        foreach (var user in users)
        {
            result.Add(new ManagedUser(
                user.Id,
                user.DisplayName,
                user.Email ?? user.UserName ?? string.Empty,
                user.IsActive,
                user.CreatedAtUtc,
                (await userManager.GetRolesAsync(user)).ToArray()));
        }

        return result;
    }

    public async Task<ManagedUser> CreateAsync(
        CreateManagedUserInput input,
        string administratorUserId)
    {
        ValidateRoles(input.SelectedRoles);
        if (input.Password != input.ConfirmPassword)
        {
            throw new UserAdministrationException(
                "The passwords do not match.");
        }

        var email = input.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new UserAdministrationException(
                "An account with that email address already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = input.DisplayName.Trim(),
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var createResult = await userManager.CreateAsync(
            user,
            input.Password);
        EnsureSuccess(
            createResult,
            "The user account could not be created.");

        var roleResult = await userManager.AddToRolesAsync(
            user,
            input.SelectedRoles);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            EnsureSuccess(
                roleResult,
                "The account roles could not be assigned.");
        }

        logger.LogInformation(
            "GHOS administrator {AdministratorUserId} created user {UserId} with roles {Roles}.",
            administratorUserId,
            user.Id,
            string.Join(",", input.SelectedRoles));
        return await ToManagedUserAsync(user);
    }

    public async Task<ManagedUser> UpdateAsync(
        string userId,
        EditManagedUserInput input,
        string administratorUserId)
    {
        ValidateRoles(input.SelectedRoles);
        var user = await RequireUserAsync(userId);
        var currentRoles = await userManager.GetRolesAsync(user);
        var isSelf = user.Id == administratorUserId;

        if (isSelf && !input.IsActive)
        {
            throw new UserAdministrationException(
                "You cannot deactivate your own account.");
        }
        if (isSelf &&
            currentRoles.Contains(GhosRoles.Administrator) &&
            !input.SelectedRoles.Contains(
                GhosRoles.Administrator,
                StringComparer.Ordinal))
        {
            throw new UserAdministrationException(
                "You cannot remove your own Administrator role.");
        }

        var removesAdministrator =
            currentRoles.Contains(GhosRoles.Administrator) &&
            (!input.IsActive ||
             !input.SelectedRoles.Contains(
                 GhosRoles.Administrator,
                 StringComparer.Ordinal));
        if (removesAdministrator &&
            await IsLastActiveAdministratorAsync(user.Id))
        {
            throw new UserAdministrationException(
                "GHOS must retain at least one active administrator.");
        }

        user.DisplayName = input.DisplayName.Trim();
        user.IsActive = input.IsActive;
        var updateResult = await userManager.UpdateAsync(user);
        EnsureSuccess(
            updateResult,
            "The user profile could not be updated.");

        var rolesToRemove = currentRoles
            .Except(
                input.SelectedRoles,
                StringComparer.Ordinal)
            .ToArray();
        var rolesToAdd = input.SelectedRoles
            .Except(
                currentRoles,
                StringComparer.Ordinal)
            .ToArray();

        if (rolesToAdd.Length > 0)
        {
            EnsureSuccess(
                await userManager.AddToRolesAsync(
                    user,
                    rolesToAdd),
                "One or more new roles could not be assigned.");
        }
        if (rolesToRemove.Length > 0)
        {
            EnsureSuccess(
                await userManager.RemoveFromRolesAsync(
                    user,
                    rolesToRemove),
                "One or more existing roles could not be removed.");
        }

        if (!user.IsActive ||
            rolesToRemove.Length > 0 ||
            rolesToAdd.Length > 0)
        {
            EnsureSuccess(
                await userManager.UpdateSecurityStampAsync(user),
                "The user session could not be refreshed.");
        }

        logger.LogInformation(
            "GHOS administrator {AdministratorUserId} updated user {UserId}; active={IsActive}; roles={Roles}.",
            administratorUserId,
            user.Id,
            user.IsActive,
            string.Join(",", input.SelectedRoles));
        return await ToManagedUserAsync(user);
    }

    public async Task ResetPasswordAsync(
        string userId,
        ResetManagedUserPasswordInput input,
        string administratorUserId)
    {
        if (input.Password != input.ConfirmPassword)
        {
            throw new UserAdministrationException(
                "The passwords do not match.");
        }

        var user = await RequireUserAsync(userId);
        var token = await userManager.GeneratePasswordResetTokenAsync(
            user);
        var result = await userManager.ResetPasswordAsync(
            user,
            token,
            input.Password);
        EnsureSuccess(
            result,
            "The password could not be reset.");
        await userManager.UpdateSecurityStampAsync(user);

        logger.LogInformation(
            "GHOS administrator {AdministratorUserId} reset the password for user {UserId}.",
            administratorUserId,
            user.Id);
    }

    private async Task<ApplicationUser> RequireUserAsync(
        string userId)
    {
        return await userManager.FindByIdAsync(userId) ??
            throw new UserAdministrationException(
                "That GHOS user no longer exists.");
    }

    private async Task<ManagedUser> ToManagedUserAsync(
        ApplicationUser user) =>
        new(
            user.Id,
            user.DisplayName,
            user.Email ?? user.UserName ?? string.Empty,
            user.IsActive,
            user.CreatedAtUtc,
            (await userManager.GetRolesAsync(user)).ToArray());

    private async Task<bool> IsLastActiveAdministratorAsync(
        string excludedUserId)
    {
        var administrators =
            await userManager.GetUsersInRoleAsync(
                GhosRoles.Administrator);
        return !administrators.Any(user =>
            user.IsActive &&
            user.Id != excludedUserId);
    }

    private static void ValidateRoles(
        IReadOnlyCollection<string> roles)
    {
        if (roles.Count == 0)
        {
            throw new UserAdministrationException(
                "Select at least one role.");
        }
        if (roles.Any(role =>
            !GhosRoles.All.Contains(
                role,
                StringComparer.Ordinal)))
        {
            throw new UserAdministrationException(
                "One or more selected roles are not valid.");
        }
    }

    private static void EnsureSuccess(
        IdentityResult result,
        string fallbackMessage)
    {
        if (result.Succeeded)
        {
            return;
        }

        var details = string.Join(
            " ",
            result.Errors.Select(error => error.Description));
        throw new UserAdministrationException(
            string.IsNullOrWhiteSpace(details)
                ? fallbackMessage
                : details);
    }
}

public sealed class CreateManagedUserInput
{
    [Required(ErrorMessage = "Enter the staff member's name.")]
    [MaxLength(160)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an email address.")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a temporary password.")]
    [MinLength(12)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm the temporary password.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public List<string> SelectedRoles { get; set; } =
        [GhosRoles.Operations];
}

public sealed class EditManagedUserInput
{
    [Required]
    [MaxLength(160)]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public List<string> SelectedRoles { get; set; } = [];
}

public sealed class ResetManagedUserPasswordInput
{
    [Required]
    [MinLength(12)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string ConfirmPassword { get; set; } = string.Empty;
}
