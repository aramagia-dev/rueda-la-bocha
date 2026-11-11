using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public sealed class BootstrapDevAccountsTests
{
    [Fact]
    public async Task Testing_no_autorizado_para_dev_bootstrap_aun_con_optin()
    {
        using var app = new TestApp();
        _ = app.Services; // Fuerza creación del host + migraciones antes de acceso directo al archivo.
        var beforeUsers = app.Scalar("SELECT COUNT(*) FROM AspNetUsers");
        bool adminCreated, userCreated;
        using (var scope = app.Services.CreateScope())
        {
            // Mismo opt-in exacto que en Development, pero entorno Testing: debe rechazar.
            var provisioner = DevProvisioner(scope, app.Database, Dir(app.Database), "Testing", app.Database, Dir(app.Database));
            (adminCreated, userCreated) = await provisioner.EnsureDevAccountsAsync();
        }
        Assert.False(adminCreated);
        Assert.False(userCreated);
        Assert.Equal(beforeUsers, app.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
    }

    [Fact]
    public async Task Dev_only_con_db_exacta_crea_idempotente_y_login_real()
    {
        using var app = new TestApp();
        _ = app.Services; // Fuerza creación del host + migraciones antes de acceso directo al archivo.
        string adminId, userId;
        using (var scope = app.Services.CreateScope())
        {
            var provisioner = DevProvisioner(scope, app.Database, Dir(app.Database), "Development", app.Database, Dir(app.Database));
            var (adminCreated, userCreated) = await provisioner.EnsureDevAccountsAsync();
            Assert.True(adminCreated);
            Assert.True(userCreated);
        }
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var admin = await users.FindByEmailAsync("admin@gmail.com");
            var user = await users.FindByEmailAsync("user@gmail.com");
            Assert.NotNull(admin);
            Assert.NotNull(user);
            Assert.True(admin!.EmailConfirmed);
            Assert.True(user!.EmailConfirmed);
            Assert.True(await users.IsInRoleAsync(admin, "Administrator"));
            Assert.False(await users.IsInRoleAsync(user, "Administrator"));
            adminId = admin.Id;
            userId = user.Id;
        }
        // Login real con las credenciales exactas aprobadas (temporal 176/178).
        using (var client = app.Client())
        {
            var login = await CourtFlowTests.Post(client, "/Identity/Account/Login",
                new() { ["Input.Email"] = "admin@gmail.com", ["Input.Password"] = "admin" });
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }
        // Idempotencia: segunda corrida no crea, no muta, ids intactos.
        var beforeUsers = app.Scalar("SELECT COUNT(*) FROM AspNetUsers");
        var beforeRoles = app.Scalar("SELECT COUNT(*) FROM AspNetUserRoles");
        using (var scope = app.Services.CreateScope())
        {
            var provisioner = DevProvisioner(scope, app.Database, Dir(app.Database), "Development", app.Database, Dir(app.Database));
            var (adminCreated, userCreated) = await provisioner.EnsureDevAccountsAsync();
            Assert.False(adminCreated);
            Assert.False(userCreated);
        }
        Assert.Equal(beforeUsers, app.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        Assert.Equal(beforeRoles, app.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.Equal(adminId, (await users.FindByEmailAsync("admin@gmail.com"))!.Id);
            Assert.Equal(userId, (await users.FindByEmailAsync("user@gmail.com"))!.Id);
        }
    }

    [Fact]
    public async Task Dev_bootstrap_rechaza_otro_db_y_no_dev_sin_mutacion()
    {
        using var app = new TestApp();
        _ = app.Services; // Fuerza creación del host + migraciones antes de acceso directo al archivo.
        var otherDir = Path.Combine(Path.GetTempPath(), "rlb-devtest-" + Guid.NewGuid());
        Directory.CreateDirectory(otherDir);
        try
        {
            var beforeUsers = app.Scalar("SELECT COUNT(*) FROM AspNetUsers");
            // Expected apunta a otro DB distinto del runtime → bloqueo.
            using (var scope = app.Services.CreateScope())
            {
                var provisioner = DevProvisioner(scope, app.Database, Dir(app.Database), "Development",
                    Path.Combine(otherDir, "other.db"), otherDir);
                var (adminCreated, userCreated) = await provisioner.EnsureDevAccountsAsync();
                Assert.False(adminCreated);
                Assert.False(userCreated);
            }
            // Entorno no Development (Production) → bloqueo aunque el opt-in sea correcto.
            using (var scope = app.Services.CreateScope())
            {
                var provisioner = DevProvisioner(scope, app.Database, Dir(app.Database), "Production", app.Database, Dir(app.Database));
                var (adminCreated, userCreated) = await provisioner.EnsureDevAccountsAsync();
                Assert.False(adminCreated);
                Assert.False(userCreated);
            }
            Assert.Equal(beforeUsers, app.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        }
        finally
        {
            if (Directory.Exists(otherDir)) Directory.Delete(otherDir, recursive: true);
        }
    }

    [Fact]
    public async Task Colision_legitima_no_se_promueve_ni_resetea_y_se_reporta()
    {
        using var app = new TestApp();
        _ = app.Services; // Fuerza creación del host + migraciones antes de acceso directo al archivo.
        string hashBefore;
        using (var scope = app.Services.CreateScope())
        {
            // Cuenta legítima preexistente con el mismo email, sin rol admin.
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var legit = new IdentityUser { UserName = "admin@gmail.com", Email = "admin@gmail.com", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(legit, TestApp.Password)).Succeeded);
            hashBefore = (await users.FindByEmailAsync("admin@gmail.com"))!.PasswordHash!;
        }
        bool adminCreated, userCreated;
        using (var scope = app.Services.CreateScope())
        {
            var provisioner = DevProvisioner(scope, app.Database, Dir(app.Database), "Development", app.Database, Dir(app.Database));
            (adminCreated, userCreated) = await provisioner.EnsureDevAccountsAsync();
        }
        // admin colisiona: no se promueve ni resetea (se reporta False); user sí se crea.
        Assert.False(adminCreated);
        Assert.True(userCreated);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var kept = (await users.FindByEmailAsync("admin@gmail.com"))!;
            Assert.Equal(hashBefore, kept.PasswordHash);
            Assert.False(await users.IsInRoleAsync(kept, "Administrator"));
            var createdUser = await users.FindByEmailAsync("user@gmail.com");
            Assert.NotNull(createdUser);
            Assert.False(await users.IsInRoleAsync(createdUser!, "Administrator"));
        }
    }

    private static string Dir(string db) => Path.GetDirectoryName(db)!;

    private static AdminProvisioner DevProvisioner(IServiceScope scope, string database, string allowedRoot,
        string environment, string expectedDb, string expectedRoot)
    {
        var sp = scope.ServiceProvider;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={database}",
            ["BootstrapDev:ExpectedDatabase"] = expectedDb,
            ["BootstrapDev:AllowedRoot"] = expectedRoot,
        }).Build();
        return new AdminProvisioner(
            sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<UserManager<IdentityUser>>(),
            sp.GetRequiredService<RoleManager<IdentityRole>>(),
            config,
            new FakeWebHostEnvironment(environment, ContentRoot()));
    }

    private static string ContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "RuedaLaBocha.Web", "RuedaLaBocha.Web.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("No se encontró src/RuedaLaBocha.Web.");
    }

    private sealed class FakeWebHostEnvironment(string environment, string contentRoot) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "RuedaLaBocha.Web";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
