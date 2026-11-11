using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;

namespace RuedaLaBocha.Web.Security;

public sealed class AdminProvisioner(ApplicationDbContext db, UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles, IConfiguration configuration, IWebHostEnvironment environment)
{
    // Credenciales exactas temporales aprobadas por el usuario (turnos 176/178, alcance 175/177).
    // Se mantienen hardcodeadas ONLY para --bootstrap-dev-accounts en Development con DB exacta
    // autorizada; no pasar a secretos contra requisito expreso. Documentado como temporal.
    public const string DevAdminEmail = "admin@gmail.com";
    public const string DevAdminPassword = "admin";
    public const string DevUserEmail = "user@gmail.com";
    public const string DevUserPassword = "user";

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

    public async Task<(bool AdminCreated, bool UserCreated)> EnsureDevAccountsAsync()
    {
        // ONLY Development estricto. Testing NO autorizado para este bootstrap.
        if (!environment.IsDevelopment()) return (false, false);
        if (!IsExactAuthorizedDb()) return (false, false);
        var adminCreated = await EnsureExactAccountAsync(DevAdminEmail, DevAdminPassword, administrator: true);
        var userCreated = await EnsureExactAccountAsync(DevUserEmail, DevUserPassword, administrator: false);
        return (adminCreated, userCreated);
    }

    private async Task<bool> EnsureExactAccountAsync(string email, string password, bool administrator)
    {
        if (!new EmailAddressAttribute().IsValid(email)) return false;
        // Bloqueo claro ante colisión: cuenta legítima existente con mismo email nunca se muta,
        // promueve ni resetea. Se reporta como no creada (el llamador devuelve exit no cero).
        var existing = await users.FindByEmailAsync(email) ?? await users.FindByNameAsync(email);
        if (existing is not null)
        {
            if (administrator) return false;
            if (await users.IsInRoleAsync(existing, AppRoles.Administrator)) return false;
            return false;
        }
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        foreach (var validator in users.UserValidators)
            if (!(await validator.ValidateAsync(users, user)).Succeeded) return false;
        // Excepción password ONLY bootstrap: hash débil con Identity, política global intacta.
        user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!(await users.CreateAsync(user)).Succeeded) return false;
        if (administrator)
        {
            if (!await roles.RoleExistsAsync(AppRoles.Administrator) &&
                !(await roles.CreateAsync(new IdentityRole(AppRoles.Administrator))).Succeeded) return false;
            if (!(await users.AddToRoleAsync(user, AppRoles.Administrator)).Succeeded) return false;
        }
        await transaction.CommitAsync();
        return true;
    }

    private bool IsExactAuthorizedDb()
    {
        try
        {
            // Opt-in explícito: sin ExpectedDatabase + AllowedRoot no hay bootstrap.
            var expected = configuration["BootstrapDev:ExpectedDatabase"]?.Trim();
            var allowedRoot = configuration["BootstrapDev:AllowedRoot"]?.Trim();
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(allowedRoot)) return false;
            var cs = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return false;
            var actualSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource?.Trim();
            if (string.IsNullOrEmpty(actualSource) ||
                string.Equals(actualSource, ":memory:", StringComparison.OrdinalIgnoreCase)) return false;
            var actualFull = Path.GetFullPath(actualSource, environment.ContentRootPath);
            var expectedFull = Path.GetFullPath(expected, environment.ContentRootPath);
            var allowedFull = Path.GetFullPath(allowedRoot, environment.ContentRootPath);
            var tempFull = Path.GetFullPath(Path.GetTempPath());
            // El root permitido debe ser dedicado bajo temp (p. ej. rlb-aislado), nunca prod.
            if (!allowedFull.StartsWith(tempFull, StringComparison.OrdinalIgnoreCase) || allowedFull.Length <= tempFull.Length)
                return false;
            // La DB runtime debe ser EXACTAMENTE la autorizada y estar bajo el root dedicado.
            return string.Equals(actualFull, expectedFull, StringComparison.OrdinalIgnoreCase)
                && actualFull.StartsWith(allowedFull, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
