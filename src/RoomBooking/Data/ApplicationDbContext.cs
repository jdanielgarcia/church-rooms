using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RoomBooking.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<SkippedDate> SkippedDates => Set<SkippedDate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Booking>(b =>
        {
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.RoomId, x.Status });
            b.HasOne(x => x.Owner).WithMany().OnDelete(DeleteBehavior.Restrict);
            // A Room with booking history can't be deleted, only deactivated (CONTEXT.md, Q13).
            b.HasOne(x => x.Room).WithMany().OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ReplacesBooking).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SkippedDate>()
            .HasIndex(x => new { x.BookingId, x.OccurrenceStartUtc })
            .IsUnique();

        builder.Entity<ApplicationUser>()
            .Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
    }
}
