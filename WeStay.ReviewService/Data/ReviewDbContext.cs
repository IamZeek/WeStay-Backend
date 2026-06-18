using Microsoft.EntityFrameworkCore;
using WeStay.ReviewService.Models;

namespace WeStay.ReviewService.Data
{
    public class ReviewDbContext : DbContext
    {
        public ReviewDbContext(DbContextOptions<ReviewDbContext> options) : base(options)
        {
        }

        public DbSet<Review> Reviews { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // One review per booking (a guest who books the same place twice can review each stay).
            modelBuilder.Entity<Review>()
                .HasIndex(r => r.BookingId)
                .IsUnique();

            modelBuilder.Entity<Review>()
                .HasIndex(r => r.ListingId);

            modelBuilder.Entity<Review>()
                .HasIndex(r => r.ReviewerId);
        }
    }
}
