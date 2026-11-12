using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Court> Courts => Set<Court>();

    public DbSet<CourtSchedule> CourtSchedules => Set<CourtSchedule>();

    public DbSet<CourtClosure> CourtClosures => Set<CourtClosure>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public DbSet<PadiPayment> PadiPayments => Set<PadiPayment>();

    public DbSet<BookingHold> BookingHolds => Set<BookingHold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CourtSchedule>().HasIndex(schedule => new { schedule.CourtId, schedule.DayOfWeek });
        modelBuilder.Entity<CourtClosure>().HasIndex(closure => new { closure.CourtId, closure.Date });
        modelBuilder.Entity<Booking>(b =>
        {
            b.Property(x => x.UserId).IsRequired().HasMaxLength(450);
            b.Property(x => x.PriceArs).HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            b.HasOne(x => x.Court).WithMany().HasForeignKey(x => x.CourtId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.CourtId, x.Date, x.StartTime })
                .IsUnique()
                .HasFilter("Status = 0");
            b.HasIndex(x => new { x.UserId, x.Date, x.Status });
            b.HasIndex(x => new { x.Date, x.Status });
        });
        modelBuilder.Entity<PadiPayment>(p =>
        {
            p.Property(x => x.Currency).IsRequired().HasMaxLength(8);
            p.Property(x => x.AmountArs).HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            p.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Restrict);
            p.HasIndex(x => x.BookingId).IsUnique();
        });
        modelBuilder.Entity<BookingHold>(h =>
        {
            h.Property(x => x.UserId).IsRequired().HasMaxLength(450);
            h.Property(x => x.Currency).IsRequired().HasMaxLength(8);
            h.Property(x => x.AmountArs).HasColumnType("decimal(18,2)").HasPrecision(18, 2);
            h.Property(x => x.PadiLinkId).HasMaxLength(64);
            h.Property(x => x.PadiPaymentId).HasMaxLength(64);
            h.Property(x => x.CheckoutUrl).HasMaxLength(512);
            h.Property(x => x.QrCodeUrl).HasMaxLength(512);
            h.HasOne(x => x.Court).WithMany().HasForeignKey(x => x.CourtId)
                .OnDelete(DeleteBehavior.Restrict);
            h.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Restrict);
            h.HasIndex(x => new { x.CourtId, x.Date, x.StartTime })
                .IsUnique()
                .HasFilter("Status = 0");
            h.HasIndex(x => new { x.UserId, x.Date, x.Status });
        });
    }
}
