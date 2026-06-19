namespace WeStay.Tests
{
    // Admin moderation across AuthService (KYC), ListingService (listing moderation), and
    // BookingService (all-bookings overview). Every endpoint is Admin-only; the suite logs in as
    // the seeded admin for the positive paths and as a fresh Guest for the 403 path.
    public class AdminModerationTests
    {
        // KYC: admin lists the Pending queue, approves one, and rejects another with a reason that persists.
        [Fact]
        public async Task Kyc_Admin_CanListPending_Approve_And_RejectWithReason()
        {
            using var api = new ApiClient();
            var adminToken = await Flows.LoginAdminAsync(api);

            // User A submits a document → admin approves it.
            var (emailA, _, tokenA) = await Flows.RegisterAsync(api);
            await Flows.SubmitVerificationAsync(api, tokenA, "DOC-A-123");

            var pendingA = await Json.ReadAsync<AdminVerificationListResponse>(
                await api.GetAsync("/api/auth/admin/verifications?status=Pending&pageSize=100", adminToken));
            var itemA = pendingA.Items.FirstOrDefault(i => i.UserEmail == emailA);
            Assert.NotNull(itemA);

            (await api.PostAsync($"/api/auth/admin/verifications/{itemA!.Id}/approve", null, adminToken))
                .EnsureSuccessStatusCode();
            var detailA = await Json.ReadAsync<AdminVerificationDetail>(
                await api.GetAsync($"/api/auth/admin/verifications/{itemA.Id}", adminToken));
            Assert.Equal("Approved", detailA.Status);

            // User B submits → admin rejects with a reason → reason is persisted.
            var (emailB, _, tokenB) = await Flows.RegisterAsync(api);
            await Flows.SubmitVerificationAsync(api, tokenB, "DOC-B-456");

            var pendingB = await Json.ReadAsync<AdminVerificationListResponse>(
                await api.GetAsync("/api/auth/admin/verifications?status=Pending&pageSize=100", adminToken));
            var itemB = pendingB.Items.FirstOrDefault(i => i.UserEmail == emailB);
            Assert.NotNull(itemB);

            const string reason = "Document image is blurry.";
            (await api.PostAsync($"/api/auth/admin/verifications/{itemB!.Id}/reject",
                new { RejectionReason = reason }, adminToken)).EnsureSuccessStatusCode();

            var detailB = await Json.ReadAsync<AdminVerificationDetail>(
                await api.GetAsync($"/api/auth/admin/verifications/{itemB.Id}", adminToken));
            Assert.Equal("Rejected", detailB.Status);
            Assert.Equal(reason, detailB.RejectionReason);
        }

        // Listing moderation: admin sees a listing it doesn't own, force-deactivates it (drops out of
        // public reads), and reactivates it.
        [Fact]
        public async Task Listings_Admin_CanListAll_ForceDeactivate_AndReactivate()
        {
            using var api = new ApiClient();
            var adminToken = await Flows.LoginAdminAsync(api);

            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);

            // Admin oversight view includes the host's listing (not owned by admin).
            var all = await Json.ReadAsync<AdminListingsResponse>(
                await api.GetAsync("/api/listings/admin/all?pageSize=100", adminToken));
            var item = all.Items.FirstOrDefault(i => i.Id == listing.Id);
            Assert.NotNull(item);
            Assert.NotEqual(0, item!.HostId); // owner info present

            // Force-deactivate → public GET is Active-only, so it 404s.
            (await api.PostAsync($"/api/listings/{listing.Id}/admin/deactivate",
                new { Reason = "Policy violation (test)" }, adminToken)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/listings/{listing.Id}")).StatusCode);

            // Reactivate → public GET works again.
            (await api.PostAsync($"/api/listings/{listing.Id}/admin/reactivate", null, adminToken))
                .EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync($"/api/listings/{listing.Id}")).StatusCode);
        }

        // Admin-ban override gap: once an admin bans a listing, the owner must NOT be able to
        // self-reactivate it via the owner status endpoint — only admin reactivate can.
        [Fact]
        public async Task Listings_OwnerCannotReactivateAdminBannedListing()
        {
            using var api = new ApiClient();
            var adminToken = await Flows.LoginAdminAsync(api);

            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);

            // Admin force-deactivates (Banned).
            (await api.PostAsync($"/api/listings/{listing.Id}/admin/deactivate",
                new { Reason = "Policy violation (test)" }, adminToken)).EnsureSuccessStatusCode();

            // Owner tries to flip it back to Active via the owner status endpoint → rejected (403),
            // with a clear message — not a silent no-op.
            var ownerAttempt = await api.PatchAsync($"/api/listings/{listing.Id}/status",
                new { Status = 0 /* ListingStatus.Active */ }, hostToken);
            Assert.Equal(HttpStatusCode.Forbidden, ownerAttempt.StatusCode);
            Assert.Contains("administrator", await ownerAttempt.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            // Owner also can't soft-delete it (which would flip Banned → Inactive as a backdoor) → 403.
            var ownerDelete = await api.DeleteAsync($"/api/listings/{listing.Id}", hostToken);
            Assert.Equal(HttpStatusCode.Forbidden, ownerDelete.StatusCode);
            Assert.Contains("administrator", await ownerDelete.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            // Still banned → public GET is Active-only, so it 404s.
            Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/listings/{listing.Id}")).StatusCode);

            // Admin reactivate still works.
            (await api.PostAsync($"/api/listings/{listing.Id}/admin/reactivate", null, adminToken))
                .EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync($"/api/listings/{listing.Id}")).StatusCode);
        }

        // Bookings overview: admin sees bookings made by different users in one list.
        [Fact]
        public async Task Bookings_Admin_CanListAll_AcrossUsers()
        {
            using var api = new ApiClient();
            var adminToken = await Flows.LoginAdminAsync(api);

            var (_, _, hostReg) = await Flows.RegisterAsync(api);
            var hostToken = await Flows.BecomeHostAsync(api, hostReg);
            var listing1 = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);
            var listing2 = await Flows.CreateListingAsync(api, hostToken, ListingCategory.ShortTerm);

            var (_, _, guest1) = await Flows.RegisterAsync(api);
            var (_, _, guest2) = await Flows.RegisterAsync(api);
            var b1 = await Flows.CreateBookingAsync(api, guest1, listing1.Id);
            var b2 = await Flows.CreateBookingAsync(api, guest2, listing2.Id);

            var all = await Json.ReadAsync<AdminBookingsResponse>(
                await api.GetAsync("/api/bookings/admin/all?pageSize=100", adminToken));

            var b1Item = all.Items.FirstOrDefault(i => i.Id == b1);
            var b2Item = all.Items.FirstOrDefault(i => i.Id == b2);
            Assert.NotNull(b1Item);
            Assert.NotNull(b2Item);
            Assert.NotEqual(b1Item!.UserId, b2Item!.UserId); // full picture, not single-user-scoped
        }

        // Every admin-prefixed endpoint rejects a non-admin (Guest) with 403.
        [Fact]
        public async Task NonAdmin_Gets403_OnAllAdminEndpoints()
        {
            using var api = new ApiClient();
            var (_, _, guestToken) = await Flows.RegisterAsync(api);

            var calls = new (HttpMethod method, string path)[]
            {
                (HttpMethod.Get,  "/api/auth/admin/verifications"),
                (HttpMethod.Get,  "/api/auth/admin/verifications/1"),
                (HttpMethod.Post, "/api/auth/admin/verifications/1/approve"),
                (HttpMethod.Post, "/api/auth/admin/verifications/1/reject"),
                (HttpMethod.Get,  "/api/listings/admin/all"),
                (HttpMethod.Post, "/api/listings/1/admin/deactivate"),
                (HttpMethod.Post, "/api/listings/1/admin/reactivate"),
                (HttpMethod.Get,  "/api/bookings/admin/all"),
            };

            foreach (var (method, path) in calls)
            {
                // Authorization (gateway RouteClaimsRequirement or service [Authorize(Roles=Admin)])
                // fires before any body validation, so a null body is fine here.
                var resp = method == HttpMethod.Get
                    ? await api.GetAsync(path, guestToken)
                    : await api.PostAsync(path, null, guestToken);

                Assert.True(resp.StatusCode == HttpStatusCode.Forbidden,
                    $"{method} {path} expected 403 but got {(int)resp.StatusCode}");
            }
        }
    }
}
