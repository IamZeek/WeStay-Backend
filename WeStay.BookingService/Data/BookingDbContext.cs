using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;
using WeStay.BookingService.Models;

namespace WeStay.BookingService.Data
{
    public class BookingDbContext : DbContext
    {
        public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options)
        {
        }

        public DbSet<Booking> Bookings { get; set; }
        public DbSet<BookingStatus> BookingStatuses { get; set; }
        public DbSet<BookingGuest> BookingGuests { get; set; }
        public DbSet<BookingPayment> BookingPayments { get; set; }
        public DbSet<PlatformFeeConfig> PlatformFeeConfigs { get; set; }
        public DbSet<Payment> Payments { get; set; }
        // BookingReviews DbSet moved to /Future (Phase 3 — Reviews). Excluded from the active model.

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure relationships
            modelBuilder.Entity<Booking>()
                .HasOne(b => b.Status)
                .WithMany()
                .HasForeignKey(b => b.StatusId);

            modelBuilder.Entity<BookingGuest>()
                .HasOne(bg => bg.Booking)
                .WithMany(b => b.Guests)
                .HasForeignKey(bg => bg.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BookingPayment>()
                .HasOne(bp => bp.Booking)
                .WithMany(b => b.Payments)
                .HasForeignKey(bp => bp.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // BookingReview relationship moved to /Future (Phase 3 — Reviews).

            // SafePay payment: one per booking (unique). Separate from the dead BookingPayments scaffold.
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Booking)
                .WithMany()
                .HasForeignKey(p => p.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.BookingId)
                .IsUnique();

            modelBuilder.Entity<Payment>()
                .HasIndex(p => p.Tracker);

            // Create indexes
            modelBuilder.Entity<Booking>()
                .HasIndex(b => b.UserId);

            modelBuilder.Entity<Booking>()
                .HasIndex(b => b.ListingId);

            modelBuilder.Entity<Booking>()
                .HasIndex(b => b.StatusId);

            modelBuilder.Entity<Booking>()
                .HasIndex(b => b.CheckInDate);

            modelBuilder.Entity<Booking>()
                .HasIndex(b => b.CheckOutDate);

            modelBuilder.Entity<Booking>()
                .HasIndex(b => b.BookingCode)
                .IsUnique();

            modelBuilder.Entity<BookingGuest>()
                .HasIndex(bg => bg.BookingId);

            modelBuilder.Entity<BookingPayment>()
                .HasIndex(bp => bp.BookingId);

            modelBuilder.Entity<BookingPayment>()
                .HasIndex(bp => bp.PaymentIntentId);

            // Seed data
            modelBuilder.Entity<BookingStatus>().HasData(
                new BookingStatus { Id = 1, Name = "Pending", Description = "Booking is created but not confirmed" },
                new BookingStatus { Id = 2, Name = "Confirmed", Description = "Booking is confirmed and active" },
                new BookingStatus { Id = 3, Name = "Cancelled", Description = "Booking has been cancelled" },
                new BookingStatus { Id = 4, Name = "Completed", Description = "Booking has been completed successfully" },
                new BookingStatus { Id = 5, Name = "Refunded", Description = "Booking was cancelled and refunded" },
                new BookingStatus { Id = 6, Name = "Rejected", Description = "Booking was rejected by the host" }
            );

            // Single global platform-fee config. Defaults: guest 8% / host 2% (10% combined) —
            // intentionally below the ~15% status quo, per WeStay's "meaningfully cheaper" positioning.
            modelBuilder.Entity<PlatformFeeConfig>().HasData(
                new PlatformFeeConfig
                {
                    Id = 1,
                    GuestServiceFee = 8m,
                    HostPlatformFee = 2m,
                    CancellationFeePercent = 10m,
                    UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            );
        }
    }
}