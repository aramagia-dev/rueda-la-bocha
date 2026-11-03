using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;

namespace RuedaLaBocha.Web.Security;

public sealed class AdminProvisioner(ApplicationDbContext db, UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles, IConfiguration configuration)
{
    public async Task<bool> CreateAsync()
    {
        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email) || string.IsNullOrEmpty(password))
            return false;

        // This one-time operation must never promote, confirm or reset an existing account.
        if (await users.FindByEmailAsync(email) is not null || await users.FindByNameAsync(email) is not null)
            return false;

        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        foreach (var validator in users.PasswordValidators)
            if (!(await validator.ValidateAsync(users, user, password)).Succeeded) return false;
        foreach (var validator in users.UserValidators)
            if (!(await validator.ValidateAsync(users, user)).Succeeded) return false;

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!(await users.CreateAsync(user, password)).Succeeded) return false;
        if (!await roles.RoleExistsAsync(AppRoles.Administrator) &&
            !(await roles.CreateAsync(new IdentityRole(AppRoles.Administrator))).Succeeded) return false;
        if (!(await users.AddToRoleAsync(user, AppRoles.Administrator)).Succeeded) return false;
        await transaction.CommitAsync();
        return true;
    }
}
