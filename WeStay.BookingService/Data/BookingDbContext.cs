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
                new BookingStatus { Id = 5, Name = "Refunded", Description = "Booking was cancelled and refunded" }
            );
        }
    }
}