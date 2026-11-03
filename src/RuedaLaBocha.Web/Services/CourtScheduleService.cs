using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Web.Services;

public sealed class CourtScheduleService(ApplicationDbContext db)
{
    public Task<List<CourtSchedule>> ListSchedulesAsync(int courtId) => db.CourtSchedules.AsNoTracking()
        .Where(schedule => schedule.CourtId == courtId)
        .OrderBy(schedule => schedule.DayOfWeek)
        .ThenBy(schedule => schedule.StartTime)
        .ToListAsync();

    public async Task<CourtSchedule> CreateScheduleAsync(int courtId, DayOfWeek dayOfWeek,
        TimeOnly startTime, TimeOnly endTime)
    {
        await EnsureCourtExistsAsync(courtId);
        ValidateSchedule(dayOfWeek, startTime, endTime);
        if (await ScheduleOverlapsAsync(courtId, dayOfWeek, startTime, endTime))
            throw new InvalidOperationException("The schedule overlaps an existing interval.");

        var schedule = new CourtSchedule
        {
            CourtId = courtId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime
        };
        db.CourtSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule;
    }

    public async Task<bool> UpdateScheduleAsync(int scheduleId, DayOfWeek dayOfWeek,
        TimeOnly startTime, TimeOnly endTime)
    {
        var schedule = await db.CourtSchedules.SingleOrDefaultAsync(item => item.Id == scheduleId);
        if (schedule is null) return false;

        ValidateSchedule(dayOfWeek, startTime, endTime);
        if (await ScheduleOverlapsAsync(schedule.CourtId, dayOfWeek, startTime, endTime, scheduleId))
            throw new InvalidOperationException("The schedule overlaps an existing interval.");

        schedule.DayOfWeek = dayOfWeek;
        schedule.StartTime = startTime;
        schedule.EndTime = endTime;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteScheduleAsync(int scheduleId)
    {
        var schedule = await db.CourtSchedules.SingleOrDefaultAsync(item => item.Id == scheduleId);
        if (schedule is null) return false;
        db.CourtSchedules.Remove(schedule);
        await db.SaveChangesAsync();
        return true;
    }

    public Task<List<CourtClosure>> ListClosuresAsync(int courtId, DateOnly? date = null)
    {
        var query = db.CourtClosures.AsNoTracking().Where(closure => closure.CourtId == courtId);
        if (date.HasValue) query = query.Where(closure => closure.Date == date.Value);
        return query.OrderBy(closure => closure.Date).ThenBy(closure => closure.StartTime).ToListAsync();
    }

    public async Task<CourtClosure> CreateClosureAsync(int courtId, DateOnly date,
        TimeOnly startTime, TimeOnly endTime)
    {
        await EnsureCourtExistsAsync(courtId);
        ValidateRange(startTime, endTime);
        if (await ClosureOverlapsAsync(courtId, date, startTime, endTime))
            throw new InvalidOperationException("The closure overlaps an existing time block.");

        var closure = new CourtClosure
        {
            CourtId = courtId,
            Date = date,
            StartTime = startTime,
            EndTime = endTime
        };
        db.CourtClosures.Add(closure);
        await db.SaveChangesAsync();
        return closure;
    }

    public async Task<bool> UpdateClosureAsync(int closureId, DateOnly date,
        TimeOnly startTime, TimeOnly endTime)
    {
        var closure = await db.CourtClosures.SingleOrDefaultAsync(item => item.Id == closureId);
        if (closure is null) return false;

        ValidateRange(startTime, endTime);
        if (await ClosureOverlapsAsync(closure.CourtId, date, startTime, endTime, closureId))
            throw new InvalidOperationException("The closure overlaps an existing time block.");

        closure.Date = date;
        closure.StartTime = startTime;
        closure.EndTime = endTime;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteClosureAsync(int closureId)
    {
        var closure = await db.CourtClosures.SingleOrDefaultAsync(item => item.Id == closureId);
        if (closure is null) return false;
        db.CourtClosures.Remove(closure);
        await db.SaveChangesAsync();
        return true;
    }

    private Task<bool> ScheduleOverlapsAsync(int courtId, DayOfWeek dayOfWeek,
        TimeOnly startTime, TimeOnly endTime, int exceptId = 0) => db.CourtSchedules.AnyAsync(schedule =>
            schedule.CourtId == courtId && schedule.DayOfWeek == dayOfWeek && schedule.Id != exceptId &&
            schedule.StartTime < endTime && schedule.EndTime > startTime);

    private Task<bool> ClosureOverlapsAsync(int courtId, DateOnly date,
        TimeOnly startTime, TimeOnly endTime, int exceptId = 0) => db.CourtClosures.AnyAsync(closure =>
            closure.CourtId == courtId && closure.Date == date && closure.Id != exceptId &&
            closure.StartTime < endTime && closure.EndTime > startTime);

    private async Task EnsureCourtExistsAsync(int courtId)
    {
        if (!await db.Courts.AnyAsync(court => court.Id == courtId))
            throw new KeyNotFoundException("Court not found.");
    }

    private static void ValidateSchedule(DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        if (!Enum.IsDefined(typeof(DayOfWeek), dayOfWeek))
            throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        ValidateRange(startTime, endTime);
    }

    private static void ValidateRange(TimeOnly startTime, TimeOnly endTime)
    {
        if (startTime >= endTime)
            throw new ArgumentException("End time must be after start time.", nameof(endTime));
    }
}
