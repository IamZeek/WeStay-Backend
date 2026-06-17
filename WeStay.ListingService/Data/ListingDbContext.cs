using Microsoft.EntityFrameworkCore;
using WeStay.ListingService.Models;
using WeStay.ListingService.Models;

namespace WeStay.ListingService.Data
{
    public class ListingDbContext : DbContext
    {
        public ListingDbContext(DbContextOptions<ListingDbContext> options) : base(options)
        {
        }

        public DbSet<Listing> Listings { get; set; }
        public DbSet<Amenity> Amenities { get; set; }
        public DbSet<ListingImage> ListingImages { get; set; }
        // Booking ownership moved to WeStay.BookingService (Phase 1 de-duplication).
        // The Booking entity/DbSet was removed from ListingService.

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure many-to-many for Listing-Amenity
            modelBuilder.Entity<Listing>()
                .HasMany(l => l.Amenities)
                .WithMany(a => a.Listings)
                .UsingEntity<Dictionary<string, object>>(
                    "ListingAmenity",
                    j => j.HasOne<Amenity>().WithMany().HasForeignKey("AmenityId"),
                    j => j.HasOne<Listing>().WithMany().HasForeignKey("ListingId"),
                    j =>
                    {
                        j.HasKey("ListingId", "AmenityId");
                        j.ToTable("ListingAmenities");
                    });

            // Configure indexes
            modelBuilder.Entity<Listing>()
                .HasIndex(l => l.HostId);

            modelBuilder.Entity<Listing>()
                .HasIndex(l => l.Status);

            modelBuilder.Entity<Listing>()
                .HasIndex(l => new { l.City, l.Country });

            // Configure decimal precision
            modelBuilder.Entity<Listing>()
                .Property(l => l.PricePerNight)
                .HasPrecision(18, 2);

            // Seed initial amenities
            modelBuilder.Entity<Amenity>().HasData(
                new Amenity { Id = 1, Name = "Wi-Fi", Description = "Wireless internet" },
                new Amenity { Id = 2, Name = "Kitchen", Description = "Cooking facilities" },
                new Amenity { Id = 3, Name = "Air Conditioning", Description = "Air conditioning system" },
                new Amenity { Id = 4, Name = "Heating", Description = "Heating system" },
                new Amenity { Id = 5, Name = "TV", Description = "Television" },
                new Amenity { Id = 6, Name = "Pool", Description = "Swimming pool" },
                new Amenity { Id = 7, Name = "Hot Tub", Description = "Jacuzzi or hot tub" },
                new Amenity { Id = 8, Name = "Free Parking", Description = "Free parking on premises" },
                new Amenity { Id = 9, Name = "Pet Friendly", Description = "Pets allowed" },
                new Amenity { Id = 10, Name = "Smoking Allowed", Description = "Smoking permitted" }
            );
        }
    }
}