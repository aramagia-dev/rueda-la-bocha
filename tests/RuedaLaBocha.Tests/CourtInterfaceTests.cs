using System.Net;
using System.Text.RegularExpressions;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public class CourtInterfaceTests
{
    private static async Task<string> Html(HttpClient client, string path) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(path));

    [Fact]
    public async Task Home_and_catalog_show_truthful_spanish_empty_states_and_real_navigation()
    {
        using var app = new TestApp();
        using var guest = app.Client();
        foreach (var path in new[] { "/", "/Courts" })
        {
            var html = await Html(guest, path);
            Assert.Contains("lang=\"es-AR\"", html);
            Assert.Contains("Todavía no hay canchas publicadas", html);
            Assert.Contains("Fútbol 5", html);
            Assert.Contains("60 minutos", html);
            Assert.Contains("ARS 40.000", html);
            Assert.Contains("href=\"#main-content\"", html);
            Assert.Contains("id=\"main-content\"", html);
            Assert.Contains("aria-controls=\"main-navigation\"", html);
            Assert.Contains("id=\"main-navigation\"", html);
            Assert.Contains("href=\"/Courts\"", html);
            Assert.DoesNotContain("/Courts/Manage", html);
            Assert.DoesNotContain("type=\"date\"", html);
            Assert.DoesNotContain("Reservar", html);
            Assert.DoesNotContain("Mis reservas", html);
            Assert.DoesNotContain("/Payments", html);
            Assert.DoesNotContain("<article class=\"court-card\"", html);
        }
        var home = await Html(guest, "/");
        Assert.Contains("Tu próximo partido", home);
        Assert.Contains("empieza acá.", home);
        Assert.Contains("href=\"#el-complejo\"", home);
        Assert.Contains("id=\"el-complejo\"", home);
        Assert.Contains("href=\"/Identity/Account/Login\"", home);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/images/football-pitch.svg")).StatusCode);
    }

    [Fact]
    public async Task Home_previews_three_real_active_courts_and_catalog_shows_all_with_encoded_content()
    {
        using var app = new TestApp();
        using var admin = await app.Login(admin: true);
        foreach (var name in new[] { "Alfa", "Beta", "Delta", "Gamma" })
            Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Create",
                new() { ["Name"] = name, ["Description"] = "<em>Real description</em>" })).StatusCode);
        await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "Hidden court", ["IsActive"] = "false" });
        using var guest = app.Client();
        var home = await guest.GetStringAsync("/");
        var catalog = await guest.GetStringAsync("/Courts");
        Assert.Equal(3, Regex.Matches(home, "<article class=\"court-card\"").Count);
        Assert.Equal(4, Regex.Matches(catalog, "<article class=\"court-card\"").Count);
        foreach (var html in new[] { home, catalog })
        {
            Assert.Contains("Alfa", html);
            Assert.Contains("&lt;em&gt;Real description&lt;/em&gt;", html);
            Assert.DoesNotContain("<em>Real description</em>", html);
            Assert.DoesNotContain("Hidden court", html);
            Assert.True(html.IndexOf("Alfa", StringComparison.Ordinal) < html.IndexOf("Beta", StringComparison.Ordinal));
        }
        Assert.DoesNotContain("Gamma", home);
        Assert.Contains("Gamma", catalog);
    }

    [Fact]
    public async Task Spanish_management_forms_keep_post_validation_and_logout_behavior()
    {
        using var app = new TestApp();
        using var admin = await app.Login(admin: true);
        var manage = await Html(admin, "/Courts/Manage");
        Assert.Contains("Administrar canchas", manage);
        Assert.Contains("Nueva cancha", manage);
        Assert.Contains("Todavía no hay canchas creadas", manage);
        Assert.Contains("Cerrar sesión", manage);
        var form = await Html(admin, "/Courts/Create");
        Assert.Contains("Nombre", form);
        Assert.Contains("Descripción", form);
        Assert.Contains("Cancha activa", form);
        Assert.Contains("Guardar cancha", form);
        Assert.Contains("name=\"__RequestVerificationToken\"", form);
        var invalid = await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "" });
        Assert.Contains("Ingresa el nombre de la cancha", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Courts"));
        await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "One" });
        await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "Two", ["IsActive"] = "false" });
        manage = await Html(admin, "/Courts/Manage");
        Assert.Contains("Activa", manage);
        Assert.Contains("Inactiva", manage);
        Assert.Contains("Desactivar One", manage);
        var edit = await Html(admin, "/Courts/Edit/2");
        Assert.Contains("Editar cancha", edit);
        Assert.Contains("Guardar cambios", edit);
        var before = app.Snapshot();
        var rejectedLogout = await admin.PostAsync("/Identity/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, rejectedLogout.StatusCode);
        Assert.Equal(before, app.Snapshot());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Courts/Manage")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Manage", new(),
            "/Identity/Account/Logout?returnUrl=%2F")).StatusCode);
        var denied = await admin.GetAsync("/Courts/Manage");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("Login", denied.Headers.Location!.ToString());
        Assert.Equal(before, app.Snapshot());
    }

    [Fact]
    public async Task Localized_form_accepts_limits_and_rejects_invalid_edits_without_data_changes()
    {
        using var app = new TestApp();
        using var admin = await app.Login(admin: true);
        var name = new string('N', 100);
        var description = new string('D', 1000);
        var created = await CourtFlowTests.Post(admin, "/Courts/Create", new()
        {
            ["Name"] = name,
            ["Description"] = description,
            ["IsActive"] = "false"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Courts WHERE length(Name) = 100 AND length(Description) = 1000 AND IsActive = 0"));
        var before = app.Snapshot();
        foreach (var fields in new Dictionary<string, string>[]
        {
            new() { ["Name"] = "" },
            new() { ["Name"] = "   " },
            new() { ["Name"] = new string('N', 101) },
            new() { ["Name"] = "Valid", ["Description"] = new string('D', 1001) }
        })
        {
            var rejected = await CourtFlowTests.Post(admin, "/Courts/Edit/1", fields);
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            Assert.Contains("field-validation-error", await rejected.Content.ReadAsStringAsync());
            Assert.Equal(before, app.Snapshot());
        }
        var saved = await CourtFlowTests.Post(admin, "/Courts/Edit/1", new()
        {
            ["Name"] = name,
            ["Description"] = description,
            ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Courts WHERE length(Name) = 100 AND length(Description) = 1000 AND IsActive = 1"));
        using var guest = app.Client();
        Assert.Contains(name, await guest.GetStringAsync("/Courts"));
    }

    [Theory]
    [InlineData("/Identity/Account/Login", "Input.Email")]
    [InlineData("/Identity/Account/Register", "Input.ConfirmPassword")]
    public async Task Shared_shell_preserves_existing_identity_forms(string path, string field)
    {
        using var app = new TestApp();
        using var guest = app.Client();
        var html = await Html(guest, path);
        Assert.Contains("Ingresar", html);
        Assert.Contains("Crear cuenta", html);
        Assert.Contains($"name=\"{field}\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.DoesNotContain("/Courts/Manage", html);
    }
}
