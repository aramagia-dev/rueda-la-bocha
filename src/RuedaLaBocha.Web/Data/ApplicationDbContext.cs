using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Court> Courts => Set<Court>();

    public DbSet<CourtSchedule> CourtSchedules => Set<CourtSchedule>();

    public DbSet<CourtClosure> CourtClosures => Set<CourtClosure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CourtSchedule>().HasIndex(schedule => new { schedule.CourtId, schedule.DayOfWeek });
        modelBuilder.Entity<CourtClosure>().HasIndex(closure => new { closure.CourtId, closure.Date });
    }
}
