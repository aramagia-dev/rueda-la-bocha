using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RuedaLaBocha.Web.Controllers;
using RuedaLaBocha.Web.Data;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public class CourtFlowTests
{
    [Fact]
    public async Task Admin_can_create_edit_and_deactivate_without_deleting_history()
    {
        using var app = new TestApp();
        using var admin = await app.Login(admin: true);
        var response = await Post(admin, "/Courts/Create", new() { ["Name"] = "Court One", ["Description"] = "Covered" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Courts WHERE Name = 'Court One' AND IsActive = 1"));
        using var guest = app.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/Courts")).StatusCode);
        var disponible = await guest.GetStringAsync("/Availability?courtId=1");
        Assert.Contains("Court One", disponible);
        Assert.Contains("Fútbol 5", WebUtility.HtmlDecode(disponible));
        Assert.Contains("60 minutos", disponible);
        Assert.Contains("40.000", disponible);
        Assert.Contains("ARS", disponible);
        response = await Post(admin, "/Courts/Edit/1", new() { ["Name"] = "Court Updated", ["Description"] = "Outdoor", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Courts WHERE Name = 'Court Updated' AND Description = 'Outdoor'"));
        response = await Post(admin, "/Courts/Manage", new(), "/Courts/Deactivate/1");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Courts"));
        Assert.Equal(0L, app.Scalar("SELECT IsActive FROM Courts WHERE Id = 1"));
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/Availability?courtId=1")).StatusCode);
        Assert.Contains("Court Updated", await admin.GetStringAsync("/Courts/Manage"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonymous_and_customer_cannot_manage_courts(bool customer)
    {
        using var app = new TestApp();
        using var owner = await app.Login(admin: true);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(owner, "/Courts/Create", new() { ["Name"] = "Protected" })).StatusCode);
        using var client = customer ? await app.Login(admin: false) : app.Client();
        var before = app.Snapshot();
        foreach (var path in new[] { "/Courts/Create", "/Courts/Edit/1", "/Courts/Manage" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(customer ? "AccessDenied" : "Login", response.Headers.Location!.ToString());
        }
        foreach (var path in new[] { "/Courts/Create", "/Courts/Edit/1", "/Courts/Deactivate/1" })
        {
            var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "Intruder" }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(customer ? "AccessDenied" : "Login", response.Headers.Location!.ToString());
        }
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Courts WHERE Name = 'Protected' AND IsActive = 1"));
        Assert.Equal(before, app.Snapshot());
    }

    [Fact]
    public async Task Validation_antiforgery_and_unknown_ids_do_not_mutate_data()
    {
        using var app = new TestApp();
        using var admin = await app.Login(admin: true);
        foreach (var name in new[] { "", "   ", new string('x', 101) })
        {
            var invalid = await Post(admin, "/Courts/Create", new() { ["Name"] = name });
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            Assert.Contains("field-validation-error", await invalid.Content.ReadAsStringAsync());
        }
        var longDescription = await Post(admin, "/Courts/Create", new() { ["Name"] = "Valid", ["Description"] = new string('x', 1001) });
        Assert.Equal(HttpStatusCode.OK, longDescription.StatusCode);
        var missingToken = await admin.PostAsync("/Courts/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "Bad" }));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Courts"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/Courts/Edit/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(admin, "/Courts/Create", new() { ["Name"] = "Unknown" }, "/Courts/Edit/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(admin, "/Courts/Create", new(), "/Courts/Deactivate/999")).StatusCode);
        var created = await Post(admin, "/Courts/Create", new() { ["Name"] = "<script>alert(1)</script>", ["Price"] = "1" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT IsActive FROM Courts"));
        using var guest = app.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/Courts")).StatusCode);
        var managed = await admin.GetStringAsync("/Courts/Manage");
        Assert.DoesNotContain("<script>alert(1)</script>", managed);
        Assert.Contains("&lt;script&gt;", managed);
        var before = app.Snapshot();
        foreach (var path in new[] { "/Courts/Edit/1", "/Courts/Deactivate/1" })
        {
            var rejected = await admin.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "Tampered" }));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Equal(before, app.Snapshot());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/Courts/Deactivate/1")).StatusCode);
        Assert.Equal(before, app.Snapshot());
    }

    [Fact]
    public async Task Public_registration_cannot_choose_administrator_role()
    {
        using var app = new TestApp();
        using var client = app.Client();
        var response = await Post(client, "/Identity/Account/Register", new()
        {
            ["Input.Email"] = "customer@example.test",
            ["Input.Password"] = TestApp.Password,
            ["Input.ConfirmPassword"] = TestApp.Password,
            ["Role"] = "Administrator",
            ["Input.Role"] = "Administrator"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM AspNetUsers"));
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
    }

    [Fact]
    public async Task Explicit_inactive_creation_is_honored()
    {
        using var app = new TestApp();
        using var admin = await app.Login(admin: true);
        var response = await Post(admin, "/Courts/Create", new() { ["Name"] = "Inactive requested", ["IsActive"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Courts"));
        Assert.Equal(0L, app.Scalar("SELECT IsActive FROM Courts"));
        using var guest = app.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/Availability?courtId=1")).StatusCode);
    }

    [Fact]
    public async Task Malformed_active_status_is_rejected_without_writing()
    {
        using var app = new TestApp();
        using var admin = await app.Login(admin: true);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(admin, "/Courts/Create", new() { ["Name"] = "Probe" })).StatusCode);
        var before = app.Snapshot();
        foreach (var path in new[] { "/Courts/Create", "/Courts/Edit/1" })
        {
            var response = await Post(admin, path, new() { ["Name"] = "Probe", ["IsActive"] = "malformed" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, app.Snapshot());
        }
    }

    [Theory]
    [InlineData("/Home/Privacy")]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css")]
    public async Task Existing_public_routes_still_work(string path)
    {
        using var app = new TestApp();
        using var client = app.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Root_redirects_to_calendar_without_showing_courts_before_date()
    {
        // Requisito aprobado 175/176/182 reemplaza el hero legacy: / es calendario sin canchas hasta fecha.
        using var app = new TestApp();
        using var guest = app.Client();
        var root = await guest.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Contains("/Reservas", root.Headers.Location!.ToString());
        var calendar = await guest.GetStringAsync("/Reservas");
        Assert.Contains("Calendario", calendar);
        Assert.DoesNotContain("Reservar turno", calendar);
    }

    [Fact]
    public async Task Bootstrap_creates_only_a_new_admin_and_rejects_reuse_without_mutation()
    {
        using var app = new TestApp();
        _ = app.Services;
        Assert.Equal(0, await app.Bootstrap("owner@example.test", TestApp.Password));
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM AspNetUsers WHERE EmailConfirmed = 1"));
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
        var before = app.Snapshot();
        Assert.NotEqual(0, await app.Bootstrap("owner@example.test", TestApp.Password));
        Assert.Equal(before, app.Snapshot());
        using var client = app.Client();
        var login = await Post(client, "/Identity/Account/Login", new() { ["Input.Email"] = "owner@example.test", ["Input.Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Courts/Manage")).StatusCode);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("owner@example.test", "")]
    [InlineData("invalid-email", "ValidPass123!")]
    [InlineData("owner@example.test", "weak")]
    public async Task Bootstrap_rejects_invalid_configuration_without_writes(string email, string password)
    {
        using var app = new TestApp();
        _ = app.Services;
        var before = app.Snapshot();
        Assert.NotEqual(0, await app.Bootstrap(email, password));
        Assert.Equal(before, app.Snapshot());
    }

    [Fact]
    public async Task Bootstrap_never_promotes_an_existing_customer()
    {
        using var app = new TestApp();
        using var customer = await app.Login(admin: false);
        var before = app.Snapshot();
        Assert.NotEqual(0, await app.Bootstrap("customer@example.test", TestApp.Password));
        Assert.Equal(before, app.Snapshot());
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM AspNetUserRoles"));
    }

    internal static async Task<HttpResponseMessage> Post(HttpClient client, string formPath,
        Dictionary<string, string> fields, string? action = null)
    {
        var html = await client.GetStringAsync(formPath);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, $"No antiforgery token at {formPath}");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(action ?? formPath, new FormUrlEncodedContent(fields));
    }
}

internal class TestApp : WebApplicationFactory<HomeController>
{
    internal const string Password = "TestOnly-Valid123!";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rlb-tests-" + Guid.NewGuid());
    internal string Database => Path.Combine(directory, "tests.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={Database}"));
        });
    }

    protected override Microsoft.Extensions.Hosting.IHost CreateHost(Microsoft.Extensions.Hosting.IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();
        return host;
    }

    internal HttpClient Client() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    internal long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    internal byte[] Snapshot()
    {
        // SQLite holds write-capable handles on Windows; share those handles when hashing.
        using var bytes = new MemoryStream();
        foreach (var path in new[] { Database, Database + "-wal" })
        {
            if (!File.Exists(path)) continue;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            file.CopyTo(bytes);
        }
        return System.Security.Cryptography.SHA256.HashData(bytes.ToArray());
    }

    internal async Task<HttpClient> Login(bool admin)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var email = admin ? "admin@example.test" : "customer@example.test";
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        if (admin)
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("Administrator"))).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Administrator")).Succeeded);
        }
        var client = Client();
        var response = await CourtFlowTests.Post(client, "/Identity/Account/Login", new() { ["Input.Email"] = email, ["Input.Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    internal async Task<int> Bootstrap(string email, string password)
    {
        var root = Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var configuration = typeof(TestApp).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "run", "--configuration", configuration, "--no-build", "--no-launch-profile", "--project", Path.Combine(root, "RuedaLaBocha.Web.csproj"), "--", "--bootstrap-admin" })
            start.ArgumentList.Add(arg);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={Database}";
        start.Environment["BootstrapAdmin__Email"] = email;
        start.Environment["BootstrapAdmin__Password"] = password;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        await Task.WhenAll(stdout, stderr);
        Assert.DoesNotContain(password.Length > 0 ? password : "DO_NOT_MATCH_EMPTY", await stdout + await stderr);
        return process.ExitCode;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
