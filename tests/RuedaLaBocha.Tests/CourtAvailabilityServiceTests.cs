using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

[Collection("Court integration")]
public sealed class CourtAvailabilityServiceTests
{
    [Fact]
    public void Migration_snapshot_matches_the_context_model()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new ApplicationDbContext(options);
        var migrations = db.GetService<IMigrationsAssembly>();
        var snapshot = migrations.ModelSnapshot!.Model;
        if (snapshot is IMutableModel mutableSnapshot) snapshot = mutableSnapshot.FinalizeModel();
        snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var designTimeModel = db.GetService<IDesignTimeModel>().Model;
        var differ = db.GetService<IMigrationsModelDiffer>();
        var designTimeRelationalModel = designTimeModel.GetRelationalModel();
        var snapshotChanges = differ.GetDifferences(snapshot.GetRelationalModel(), designTimeRelationalModel);
        var migrationType = migrations.Migrations["20261111225033_AddPadiPayments"];
        var migration = migrations.CreateMigration(migrationType, db.Database.ProviderName!);
        var targetModel = migration.TargetModel;
        if (targetModel is IMutableModel mutableTarget) targetModel = mutableTarget.FinalizeModel();
        targetModel = db.GetService<IModelRuntimeInitializer>().Initialize(targetModel);
        var targetModelChanges = differ.GetDifferences(targetModel.GetRelationalModel(), designTimeRelationalModel);

        Assert.Empty(snapshotChanges.Select(change => change.GetType().Name));
        Assert.Empty(targetModelChanges.Select(change => change.GetType().Name));
    }

    [Fact]
    public async Task Weekly_schedules_are_persisted_and_reject_invalid_or_overlapping_ranges()
    {
        using var app = new TestApp();
        var courtId = await CreateCourt(app, "Schedule court");
        int morningId;
        int afternoonId;

        using (var scope = app.Services.CreateScope())
        {
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            morningId = (await schedules.CreateScheduleAsync(courtId, DayOfWeek.Monday,
                new TimeOnly(9, 0), new TimeOnly(12, 0))).Id;
            afternoonId = (await schedules.CreateScheduleAsync(courtId, DayOfWeek.Monday,
                new TimeOnly(13, 0), new TimeOnly(17, 0))).Id;

            var beforeRejectedCreates = app.Snapshot();
            await Assert.ThrowsAsync<InvalidOperationException>(() => schedules.CreateScheduleAsync(
                courtId, DayOfWeek.Monday, new TimeOnly(11, 0), new TimeOnly(14, 0)));
            await Assert.ThrowsAsync<ArgumentException>(() => schedules.CreateScheduleAsync(
                courtId, DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(17, 0)));
            Assert.Equal(beforeRejectedCreates, app.Snapshot());

            Assert.True(await schedules.UpdateScheduleAsync(morningId, DayOfWeek.Monday,
                new TimeOnly(8, 0), new TimeOnly(11, 0)));
        }

        using (var scope = app.Services.CreateScope())
        {
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            var saved = await schedules.ListSchedulesAsync(courtId);
            Assert.Equal(2, saved.Count);
            Assert.Equal(new TimeOnly(8, 0), saved.Single(schedule => schedule.Id == morningId).StartTime);
            Assert.True(await schedules.DeleteScheduleAsync(afternoonId));
        }

        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM CourtSchedules"));
    }

    [Fact]
    public async Task Dated_closures_are_persisted_and_reject_invalid_or_overlapping_ranges()
    {
        using var app = new TestApp();
        var courtId = await CreateCourt(app, "Closure court");
        var date = new DateOnly(2026, 11, 2);
        int closureId;

        using (var scope = app.Services.CreateScope())
        {
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            closureId = (await schedules.CreateClosureAsync(courtId, date,
                new TimeOnly(10, 0), new TimeOnly(11, 0))).Id;
            _ = await schedules.CreateClosureAsync(courtId, date,
                new TimeOnly(12, 0), new TimeOnly(13, 0));

            var beforeRejectedCreates = app.Snapshot();
            await Assert.ThrowsAsync<InvalidOperationException>(() => schedules.CreateClosureAsync(
                courtId, date, new TimeOnly(10, 30), new TimeOnly(11, 30)));
            await Assert.ThrowsAsync<ArgumentException>(() => schedules.CreateClosureAsync(
                courtId, date, new TimeOnly(14, 0), new TimeOnly(13, 0)));
            Assert.Equal(beforeRejectedCreates, app.Snapshot());

            Assert.True(await schedules.UpdateClosureAsync(closureId, date,
                new TimeOnly(9, 30), new TimeOnly(10, 30)));
        }

        using (var scope = app.Services.CreateScope())
        {
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            var saved = await schedules.ListClosuresAsync(courtId, date);
            Assert.Equal(2, saved.Count);
            Assert.Equal(new TimeOnly(9, 30), saved.Single(closure => closure.Id == closureId).StartTime);
            Assert.True(await schedules.DeleteClosureAsync(closureId));
        }

        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM CourtClosures"));
    }

    [Fact]
    public async Task Availability_uses_complete_hour_slots_and_excludes_dated_blocks()
    {
        using var app = new TestApp();
        var activeCourtId = await CreateCourt(app, "Active court");
        var inactiveCourtId = await CreateCourt(app, "Inactive court", isActive: false);
        var monday = new DateOnly(2026, 11, 2);

        using (var scope = app.Services.CreateScope())
        {
            var schedules = scope.ServiceProvider.GetRequiredService<CourtScheduleService>();
            await schedules.CreateScheduleAsync(activeCourtId, DayOfWeek.Monday,
                new TimeOnly(9, 0), new TimeOnly(11, 0));
            await schedules.CreateScheduleAsync(activeCourtId, DayOfWeek.Monday,
                new TimeOnly(12, 30), new TimeOnly(14, 0));
            await schedules.CreateClosureAsync(activeCourtId, monday,
                new TimeOnly(10, 30), new TimeOnly(11, 30));
            await schedules.CreateScheduleAsync(inactiveCourtId, DayOfWeek.Monday,
                new TimeOnly(9, 0), new TimeOnly(10, 0));

            var availability = scope.ServiceProvider.GetRequiredService<CourtAvailabilityService>();
            Assert.Equal(new[] { new TimeOnly(9, 0), new TimeOnly(12, 30) },
                await availability.GetAvailableStartTimesAsync(activeCourtId, monday));
            Assert.Empty(await availability.GetAvailableStartTimesAsync(activeCourtId, monday.AddDays(1)));
            Assert.Empty(await availability.GetAvailableStartTimesAsync(inactiveCourtId, monday));
        }
    }

    private static async Task<int> CreateCourt(TestApp app, string name, bool isActive = true)
    {
        using var scope = app.Services.CreateScope();
        var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
        await courts.CreateAsync(new CourtForm { Name = name, Description = "Test court", IsActive = isActive });
        return (await courts.ListAsync(includeInactive: true)).Single(court => court.Name == name).Id;
    }
}
