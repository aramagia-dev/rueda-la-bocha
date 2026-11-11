using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public class AvailabilityRegressionTests
{
    private const string Manage = "/Availability/Manage?courtId=1";

    private static Dictionary<string, string> Fields(bool closure, string start = "08:30", string end = "11:00") =>
        closure ? new() { ["Date"] = "2026-11-09", ["StartTime"] = start, ["EndTime"] = end }
                : new() { ["DayOfWeek"] = "1", ["StartTime"] = start, ["EndTime"] = end };

    private static string PathFor(bool closure) => "/Availability/" + (closure ? "Closure" : "Schedule") + "?courtId=1";

    private static async Task<HttpClient> Owner(TestApp app)
    {
        var admin = await app.Login(true);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "Probe court" })).StatusCode);
        return admin;
    }

    private static async Task<HttpResponseMessage> SubmitRendered(HttpClient client, string page, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(page);
        var form = Regex.Matches(html, "<form\\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(match => match.Value).Single(tag => tag.Contains("management-panel"));
        var action = WebUtility.HtmlDecode(Regex.Match(form, "action=\"([^\"]*)\"").Groups[1].Value);
        Assert.False(string.IsNullOrEmpty(action));
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        return await client.PostAsync(action, new FormUrlEncodedContent(fields));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rendered_edit_form_updates_one_existing_record(bool closure)
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitRendered(admin, PathFor(closure), Fields(closure))).StatusCode);
        var listing = await admin.GetStringAsync(Manage);
        var kind = closure ? "Closure" : "Schedule";
        var edit = WebUtility.HtmlDecode(Regex.Match(listing, "href=\"([^\"]*/Availability/" + kind + "/1[^\"]*)\"").Groups[1].Value);
        Assert.Equal("/Availability/" + kind + "/1?courtId=1", edit);
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitRendered(admin, edit, Fields(closure, "12:00", "13:00"))).StatusCode);
        var table = closure ? "CourtClosures" : "CourtSchedules";
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM " + table));
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM " + table + " WHERE Id=1 AND CourtId=1 AND StartTime LIKE '12:00%'"));
        Assert.Contains("12:00", await admin.GetStringAsync(edit));
        var delete = "/Availability/Delete" + kind + "/1?courtId=1";
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Manage, new(), delete)).StatusCode);
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM " + table));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejections_preserve_bytes_and_have_expected_messages(bool closure)
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        var path = PathFor(closure);
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitRendered(admin, path, Fields(closure))).StatusCode);
        var sameDay = closure ? "La finalización debe ser posterior al inicio, dentro del mismo día." : "El cierre debe ser posterior a la apertura, dentro del mismo día.";
        var overlap = closure ? "El bloqueo se superpone con otro de esta fecha." : "El horario se superpone con otro intervalo de este día.";
        foreach (var probe in new[]
        {
            ("08:30", "08:30", sameDay), ("23:00", "01:00", sameDay), ("08:30", "11:00", overlap),
            ("09:00", "10:00", overlap), ("08:00", "12:00", overlap),
            ("", "11:00", closure ? "Ingresa la hora de inicio." : "Ingresa la hora de apertura.")
        })
        {
            var before = app.Snapshot();
            var response = await SubmitRendered(admin, path, Fields(closure, probe.Item1, probe.Item2));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(probe.Item3, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
            Assert.Equal(before, app.Snapshot());
        }
        foreach (var value in new[] { "", "nonsense", "99" })
        {
            var fields = Fields(closure);
            fields[closure ? "Date" : "DayOfWeek"] = value;
            var before = app.Snapshot();
            var response = await SubmitRendered(admin, path, fields);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
            Assert.Equal(before, app.Snapshot());
        }
        foreach (var value in new[] { "nonsense", "24:00" })
        {
            var before = app.Snapshot();
            var response = await SubmitRendered(admin, path, Fields(closure, value, "11:00"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
            Assert.Equal(before, app.Snapshot());
        }
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitRendered(admin, path, Fields(closure, "11:00", "12:00"))).StatusCode);
        var bytes = app.Snapshot();
        var rejected = await SubmitRendered(admin, "/Availability/" + (closure ? "Closure" : "Schedule") + "/2?courtId=1", Fields(closure, "10:00", "12:00"));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(bytes, app.Snapshot());
    }

    [Fact]
    public async Task All_mutations_enforce_identity_authorization_and_antiforgery()
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        foreach (var closure in new[] { false, true })
            Assert.Equal(HttpStatusCode.Redirect, (await SubmitRendered(admin, PathFor(closure), Fields(closure))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "Second court" })).StatusCode);
        using var guest = app.Client();
        using var customer = await app.Login(false);
        foreach (var kind in new[] { "Schedule", "Closure", "DeleteSchedule", "DeleteClosure" })
        {
            foreach (var query in new[] { "courtId=2&id=1", "courtId=1&id=999", "courtId=999&id=1" })
            {
                var before = app.Snapshot();
                Assert.Equal(HttpStatusCode.NotFound, (await CourtFlowTests.Post(admin, Manage, Fields(false), "/Availability/" + kind + "?" + query)).StatusCode);
                Assert.Equal(before, app.Snapshot());
            }
            var path = "/Availability/" + kind + "/1?courtId=1";
            foreach (var client in new[] { admin, guest, customer })
            {
                var before = app.Snapshot();
                var response = await client.PostAsync(path, new FormUrlEncodedContent(Fields(false)));
                Assert.Equal(client == admin ? HttpStatusCode.BadRequest : HttpStatusCode.Redirect, response.StatusCode);
                if (client != admin)
                    Assert.Contains(client == guest ? "Login" : "AccessDenied", response.Headers.Location!.ToString());
                Assert.Equal(before, app.Snapshot());
            }
        }
    }

    [Fact]
    public async Task Public_output_is_exactly_the_eligible_slots()
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        using var guest = app.Client();
        foreach (var range in new[] { ("08:30", "10:45"), ("10:45", "12:45"), ("23:00", "23:59") })
            Assert.Equal(HttpStatusCode.Redirect, (await SubmitRendered(admin, PathFor(false), Fields(false, range.Item1, range.Item2))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitRendered(admin, PathFor(true), Fields(true, "09:30", "10:00"))).StatusCode);
        string[] Slots(string html) => Regex.Matches(html, "<li class=\"list-group-item\">([^<]+)</li>").Select(match => match.Groups[1].Value.Trim()).ToArray();
        const string path = "/Availability?courtId=1&date=";
        Assert.Equal(new[] { "08:30 - 09:30", "10:45 - 11:45", "11:45 - 12:45" }, Slots(await guest.GetStringAsync(path + "2026-11-09")));
        Assert.Equal(new[] { "08:30 - 09:30", "09:30 - 10:30", "10:45 - 11:45", "11:45 - 12:45" }, Slots(await guest.GetStringAsync(path + "2026-11-16")));
        Assert.Empty(Slots(await guest.GetStringAsync(path + "2026-11-10")));
        var before = app.Snapshot();
        var malformed = await guest.GetAsync(path + "2026-02-30");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("Selecciona una fecha válida.", await malformed.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Snapshot());
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "Second court" })).StatusCode);
        Assert.Empty(Slots(await guest.GetStringAsync("/Availability?courtId=2&date=2026-11-09")));
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Manage", new(), "/Courts/Deactivate/1")).StatusCode);
        before = app.Snapshot();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path + "2026-11-09")).StatusCode);
        Assert.Equal(before, app.Snapshot());
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/Courts")).StatusCode);
        Assert.Equal(3L, app.Scalar("SELECT COUNT(*) FROM CourtSchedules"));
    }

    [Fact]
    public async Task Migration_preserves_existing_rows_and_matches_snapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rlb-migration-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=" + Path.Combine(directory, "upgrade.db")).Options;
            using (var db = new ApplicationDbContext(options))
            {
                var migrations = db.GetService<IMigrator>();
                var baseline = db.Database.GetMigrations().Order().Reverse().Skip(1).First();
                await migrations.MigrateAsync(baseline);
                db.Courts.Add(new Court { Name = "Existing court", Description = "Preserved", IsActive = true });
                db.Users.Add(new IdentityUser { Id = "existing-user", UserName = "old@example.test", Email = "old@example.test", EmailConfirmed = true, SecurityStamp = "unchanged" });
                await db.SaveChangesAsync();
                await migrations.MigrateAsync();
            }
            using (var db = new ApplicationDbContext(options))
            {
                Assert.Equal("Preserved", (await db.Courts.SingleAsync()).Description);
                Assert.Equal("unchanged", (await db.Users.SingleAsync()).SecurityStamp);
                Assert.True((await db.Users.SingleAsync()).EmailConfirmed);
                Assert.Empty(await db.CourtSchedules.ToListAsync());
                Assert.Empty(await db.CourtClosures.ToListAsync());
                Assert.False(db.Database.HasPendingModelChanges());
                var assembly = db.GetService<IMigrationsAssembly>();
                foreach (var model in new[] { assembly.ModelSnapshot!.Model, assembly.CreateMigration(assembly.Migrations.Last().Value, db.Database.ProviderName!).TargetModel })
                {
                    var initialized = db.GetService<IModelRuntimeInitializer>().Initialize(model);
                    Assert.Empty(db.GetService<IMigrationsModelDiffer>().GetDifferences(initialized.GetRelationalModel(), db.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
                }
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
