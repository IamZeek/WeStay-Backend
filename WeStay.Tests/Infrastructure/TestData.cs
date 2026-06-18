namespace WeStay.Tests.Infrastructure
{
    /// <summary>
    /// Builders for request bodies. Every identity value is randomized per call so tests can run
    /// repeatedly without colliding on unique constraints (e.g. email).
    /// </summary>
    public static class TestData
    {
        public const string Password = "Passw0rd!";

        // The admin user seeded by AuthService in Development (AdminSeed defaults).
        public const string AdminEmail = "admin@westay.local";
        public const string AdminPassword = "Admin123!";

        public static string RandomEmail() => $"test_{Guid.NewGuid():N}@westay-test.com";

        public static string UniqueCity() => $"City_{Guid.NewGuid():N}";

        public static object RegisterBody(string email, string password = Password) => new
        {
            Email = email,
            Password = password,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "+923001234567"
        };

        public static object ListingBody(ListingCategory category, int guests, decimal pricePerNight, string city) => new
        {
            Title = "Test Listing",
            Description = "A listing created by the automated test suite.",
            Type = 0, // ListingType.Appartment
            Category = (int)category,
            Guests = guests,
            Bedrooms = 1,
            Beds = 1,
            Bathrooms = 1,
            PricePerNight = pricePerNight,
            Address = "1 Test Street",
            City = city,
            State = "TestState",
            Country = "PK",
            ZipCode = "00000",
            AmenityIds = new[] { 1 },
            ImageUrls = new[] { "https://example.com/image.jpg" }
        };

        public static object BookingBody(int listingId, int numberOfGuests, int checkInOffsetDays = 7) => new
        {
            ListingId = listingId,
            CheckInDate = DateTime.UtcNow.Date.AddDays(checkInOffsetDays),
            CheckOutDate = DateTime.UtcNow.Date.AddDays(checkInOffsetDays + 3),
            NumberOfGuests = numberOfGuests,
            SpecialRequests = "none",
            Guests = new[]
            {
                new
                {
                    FirstName = "Guest",
                    LastName = "One",
                    Email = "guest@example.com",
                    PhoneNumber = "+923001234567",
                    DateOfBirth = (DateTime?)null
                }
            }
        };
    }
}
