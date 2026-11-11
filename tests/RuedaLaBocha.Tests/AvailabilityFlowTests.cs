using System.Net;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public class AvailabilityFlowTests
{
    private const string Monday = "2026-11-09";
    private const string Manage = "/Availability/Manage?courtId=1";
    private const string Schedule = "/Availability/Schedule?courtId=1";
    private const string Closure = "/Availability/Closure?courtId=1";

    [Fact]
    public async Task Administrator_manages_persisted_schedules_and_closures_through_forms()
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        Assert.Contains("Horarios", await admin.GetStringAsync("/Courts/Manage"));
        Assert.Contains("Horario semanal", await admin.GetStringAsync(Manage));
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Schedule, Hours())).StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM CourtSchedules WHERE CourtId = 1 AND DayOfWeek = 1"));
        Assert.Contains("18:00", await admin.GetStringAsync(Manage));
        var edit = Schedule + "&id=1";
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, edit, Hours("19:00", "22:00"))).StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM CourtSchedules WHERE StartTime LIKE '19:00%' AND EndTime LIKE '22:00%'"));
        Assert.Contains("19:00", await admin.GetStringAsync(edit));
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Closure, Block())).StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM CourtClosures WHERE Date = '2026-11-09'"));
        edit = Closure + "&id=1";
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, edit, Block("20:00", "21:00"))).StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM CourtClosures WHERE StartTime LIKE '20:00%' AND EndTime LIKE '21:00%'"));
        Assert.Contains("20:00", await admin.GetStringAsync(edit));
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Manage, new(), "/Availability/DeleteClosure?courtId=1&id=1")).StatusCode);
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM CourtClosures"));
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Manage, new(), "/Availability/DeleteSchedule?courtId=1&id=1")).StatusCode);
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM CourtSchedules"));
        Assert.DoesNotContain("19:00", await admin.GetStringAsync(Manage));
    }

    [Fact]
    public async Task Public_availability_uses_complete_slots_weekdays_blocks_and_active_status()
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        using var guest = app.Client();
        Assert.Contains("Consultar disponibilidad", await guest.GetStringAsync("/Availability?courtId=1&date=" + Monday));
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Schedule, Hours("18:00", "20:30"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Schedule, Hours("21:00", "23:00"))).StatusCode);
        var path = "/Availability?courtId=1&date=" + Monday;
        var html = await guest.GetStringAsync(path);
        foreach (var slot in new[] { "18:00 - 19:00", "19:00 - 20:00", "21:00 - 22:00", "22:00 - 23:00" })
            Assert.Contains(slot, html);
        Assert.DoesNotContain("20:00 - 21:00", html);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Closure, Block("18:30", "19:00"))).StatusCode);
        html = await guest.GetStringAsync(path);
        Assert.DoesNotContain("18:00 - 19:00", html);
        Assert.Contains("19:00 - 20:00", html);
        Assert.Contains("18:00 - 19:00", await guest.GetStringAsync("/Availability?courtId=1&date=2026-11-16"));
        Assert.Contains("No hay turnos disponibles", await guest.GetStringAsync("/Availability?courtId=1&date=2026-11-10"));
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/Availability?courtId=1&date=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/Availability?courtId=999")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Manage", new(), "/Courts/Deactivate/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Invalid_and_overlapping_rules_preserve_the_database_and_show_errors()
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Schedule, Hours())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Closure, Block())).StatusCode);
        var before = app.Snapshot();
        var invalidHours = new[]
        {
            Hours("18:00", "18:00"), Hours("23:00", "01:00"), Hours("invalid", "22:00"),
            Hours("", "22:00"), new() { ["DayOfWeek"] = "99", ["StartTime"] = "18:00", ["EndTime"] = "22:00" },
            new() { ["StartTime"] = "18:00", ["EndTime"] = "22:00" }, Hours("19:00", "20:00")
        };
        foreach (var fields in invalidHours)
        {
            var response = await CourtFlowTests.Post(admin, Schedule, fields);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
            Assert.Equal(before, app.Snapshot());
        }
        foreach (var fields in new[] { Block("20:00", "19:00"), Block("18:30", "19:30"), Block(date: "invalid"), Block(date: "") })
        {
            var response = await CourtFlowTests.Post(admin, Closure, fields);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
            Assert.Equal(before, app.Snapshot());
        }
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Schedule, Hours("22:00", "23:00"))).StatusCode);
        before = app.Snapshot();
        var overlapEdit = await CourtFlowTests.Post(admin, Schedule + "&id=2", Hours("21:00", "23:00"));
        Assert.Contains("validation-summary-errors", await overlapEdit.Content.ReadAsStringAsync());
        Assert.Equal(before, app.Snapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonymous_and_customer_cannot_read_admin_forms_or_mutate_rules(bool customer)
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Schedule, Hours())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Closure, Block())).StatusCode);
        using var client = customer ? await app.Login(admin: false) : app.Client();
        var before = app.Snapshot();
        foreach (var path in new[] { Manage, Schedule, Schedule + "&id=1", Closure, Closure + "&id=1" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(customer ? "AccessDenied" : "Login", response.Headers.Location!.ToString());
        }
        foreach (var path in new[] { Schedule, Schedule + "&id=1", Closure, Closure + "&id=1", "/Availability/DeleteSchedule?courtId=1&id=1", "/Availability/DeleteClosure?courtId=1&id=1" })
        {
            var response = await client.PostAsync(path, new FormUrlEncodedContent(Hours()));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(customer ? "AccessDenied" : "Login", response.Headers.Location!.ToString());
            Assert.Equal(before, app.Snapshot());
        }
    }

    [Fact]
    public async Task Forgery_missing_ids_and_cross_court_records_are_rejected_without_writes()
    {
        using var app = new TestApp();
        using var admin = await Owner(app);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Schedule, Hours())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, Closure, Block())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "Other court" })).StatusCode);
        var before = app.Snapshot();
        foreach (var path in new[] { Schedule, Schedule + "&id=1", Closure, Closure + "&id=1", "/Availability/DeleteSchedule?courtId=1&id=1", "/Availability/DeleteClosure?courtId=1&id=1" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(path, new FormUrlEncodedContent(Hours()))).StatusCode);
            Assert.Equal(before, app.Snapshot());
        }
        foreach (var action in new[] { "Schedule", "Closure", "DeleteSchedule", "DeleteClosure" })
        {
            foreach (var query in new[] { "courtId=2&id=1", "courtId=1&id=999", "courtId=999&id=1" })
            {
                var path = "/Availability/" + action + "?" + query;
                var response = await CourtFlowTests.Post(admin, Manage, Hours(), path);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal(before, app.Snapshot());
                Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(path)).StatusCode);
            }
        }
    }

    private static Dictionary<string, string> Hours(string start = "18:00", string end = "22:00") =>
        new() { ["DayOfWeek"] = "1", ["StartTime"] = start, ["EndTime"] = end };

    private static Dictionary<string, string> Block(string start = "18:00", string end = "19:00", string date = Monday) =>
        new() { ["Date"] = date, ["StartTime"] = start, ["EndTime"] = end };

    private static async Task<HttpClient> Owner(TestApp app)
    {
        var admin = await app.Login(admin: true);
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(admin, "/Courts/Create", new() { ["Name"] = "Available court" })).StatusCode);
        return admin;
    }
}
