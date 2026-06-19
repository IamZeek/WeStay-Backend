# WeStay Backend — Project Status

**Last verified:** 2026-06-19
**Basis:** Inspected directly against current source code and the live remote SQL Server (all six databases). Supersedes earlier versions.

**6 active services**, each with its **own database** and EF Core **migrations** (no `EnsureCreated()` anywhere). JWT is unified across services and the gateway. An HTTP-level integration test suite (`WeStay.Tests`, through the gateway) passes **44/44**.

## Database map

| Service | Database |
|---|---|
| WeStay.AuthService | `WeStayAuth` |
| WeStay.ListingService | `WeStayListing` |
| WeStay.BookingService | `WeStayBooking` |
| WeStay.MessagingService | `WeStayMessaging` |
| WeStay.NotificationService | `WeStayNotification` |
| WeStay.ReviewService | `WeStayReview` |
| WeStay.ApiGateway | none (routing only) |
| WeStay.Common | none (empty shared library) |

---

## 1. AuthService (`WeStayAuth`)

**Endpoints** (`AuthController` — anonymous unless marked):

| Method | Route | Auth |
|---|---|---|
| POST | `/api/auth/register` | Anonymous |
| POST | `/api/auth/login` | Anonymous |
| GET | `/api/auth/external-login` / `external-login-callback` | Anonymous (Google only) |
| GET | `/api/auth/profile` | Authenticated |
| POST | `/api/auth/change-password` | Authenticated |
| POST | `/api/auth/become-host` | Authenticated (Guest → Host; returns fresh token) |
| PUT | `/api/auth/users/{id}/role` | **Admin** |
| PUT | `/api/auth/verification-update` | Authenticated |
| POST | `/api/auth/send-otp-phone` / `verify-otp-phone` / `send-otp-email` / `verify-otp-email` | Authenticated |
| GET | `/api/auth/users/{id}/contact` | **`[ServiceAuth]`** (internal service key — used by Booking/Review to address notifications) |

JWT carries a `role` claim (Guest/Host/Admin). Google OAuth is conditional (registered only when configured); Facebook removed. No stubs. Smells: phone-OTP stored in-memory (no TTL); `verify-otp-phone` binds `dynamic`. **Tracked inconsistency:** the OTP send paths call providers **directly** — phone via Twilio in `PhoneVerificationService`, email via SendGrid in `EmailService` — rather than going through NotificationService like the other event notifications. Functional, but bypasses the unified path; candidate to migrate later.

**Admin seed:** on startup AuthService idempotently ensures an admin user exists (creates it, or promotes an existing user with that email to Admin). Config keys: `AdminSeed:Email` (default `admin@westay.local`) and `AdminSeed:Password`. In **Development** a dev-default password (`Admin123!`) is used so the app/tests have an admin out of the box; in **other environments** the admin is seeded **only** when `AdminSeed:Password` is explicitly configured — a default-password admin is never seeded in production. Override both via User Secrets / env vars.

**Schema:** `Users` (incl. `Role`, verification flags, external-login fields), `ExternalLogins`, `Verifications`. Migrations: `AddInitialMigrtion`, `AddUserRole`.

---

## 2. ListingService (`WeStayListing`)

**Endpoints** — `ListingsController` (class `[Authorize]`):

| Method | Route | Auth |
|---|---|---|
| GET | `/api/listings` | Authenticated (host's own) |
| GET | `/api/listings/{id}` | Anonymous |
| GET | `/api/listings/{id}/price` | **`[ServiceAuth]`** (internal service key) |
| GET | `/api/listings/{id}/capacity` | **`[ServiceAuth]`** (internal service key) |
| GET | `/api/listings/{id}/owner` | **`[ServiceAuth]`** (internal service key) |
| PUT | `/api/listings/{id}/rating` | **`[ServiceAuth]`** (internal service key — ReviewService) |
| POST | `/api/listings/upload-image` | Authenticated |
| POST | `/api/listings` | Authenticated |
| PUT | `/api/listings/{id}` | Authenticated |
| DELETE | `/api/listings/{id}` | Authenticated (soft-delete → Inactive) |
| PATCH | `/api/listings/{id}/status` | Authenticated |
| POST | `/api/listings/{id}/feature` | **Host or Admin** |

`SearchController` (all Anonymous): `GET /api/search`, `/featured`, `/similar/{id}`, `/popular-locations`, `/stats`.

Photo upload → Azure Blob (`AzureBlobStorage:ConnectionString` required). Search filters: location, price, capacity, `type`, `category`, amenities, **map bounding-box**, pagination, sort. **Rating sort now works** (sorts by the cached `AverageRating` — the old `OrderByDescending(l => 0)` stub is gone). Featured = real query on `IsFeatured`/`FeaturedUntil`. No stubs.

**Schema:** `Listings` (incl. `Category`, `IsFeatured`/`FeaturedUntil`, `Latitude`/`Longitude`, **`AverageRating`/`ReviewCount`** [cached from ReviewService]), `ListingImages`, `Amenities`, `ListingAmenities`. Migrations: `AddInitialListingSchema`, `RemoveBookingAndAddListingFeatures`, `AddListingRatingCache`.

---

## 3. BookingService (`WeStayBooking`)

**Endpoints** — `BookingsController` (class `[Authorize]`):

| Method | Route | Auth |
|---|---|---|
| POST | `/api/bookings` | Authenticated |
| GET | `/api/bookings/{id}` | Authenticated (ownership-checked) |
| GET | `/api/bookings/code/{bookingCode}` | Authenticated |
| GET | `/api/bookings/user/{userId}` | Authenticated |
| GET | `/api/bookings/{id}/info` | **`[ServiceAuth]`** (internal service key — ReviewService) |
| POST | `/api/bookings/availability` | Anonymous |
| GET | `/api/bookings/availability-calendar/{listingId}` | Anonymous |
| POST | `/api/bookings/{id}/cancel` | Authenticated (guest/admin) |
| POST | `/api/bookings/{id}/confirm` | **Host-owner or Admin** |
| POST | `/api/bookings/{id}/reject` | **Host-owner or Admin** |
| POST | `/api/bookings/{id}/complete` | **Host-owner or Admin** |
| POST | `/api/bookings/jobs/auto-complete` | **Admin** (manual/ops trigger; also runs on a timer) |
| POST | `/api/bookings/jobs/auto-cancel` | **Admin** (manual/ops trigger; also runs on a timer) |

Create validates guest capacity (HTTP call to ListingService) and availability. Host actions verify listing ownership via `GET /api/listings/{id}/owner`. Each transition is status-guarded and returns a clear 4xx (not 500) when applied from the wrong state.

### Booking state machine

```
                  ┌─ /confirm (host) ─→ Confirmed ─┬─ /complete (host) ───────────────→ Completed ─→ (review-eligible)
   (create)       │                                └─ auto-complete job (checkout passed) ┘
  ─────────→  Pending ─┬─ /reject (host) ──────────────────→ Rejected
                       ├─ /cancel (guest/admin) ───────────→ Cancelled
                       └─ auto-cancel job (unconfirmed >24h) → Cancelled
```

- `BookingStatuses` has **6** seeded rows: Pending(1), Confirmed(2), Cancelled(3), Completed(4), Refunded(5), Rejected(6).
- `/confirm` and `/reject` only from **Pending**; `/complete` only from **Confirmed**. `/cancel` is not state-gated.
- **Completed is the gate for reviews.** It is reached **automatically** by the `BookingCompletionService` background job once `CheckOutDate` passes, or manually via `/complete` (Host/Admin).
- **Background jobs** (BookingService hosted services, both poll every `Booking:AutoJobIntervalMinutes`, default 60): **auto-complete** (Confirmed past checkout → Completed) and **auto-cancel** (Pending older than `Booking:AutoCancelPendingHours`, default 24h → Cancelled). The `POST /api/bookings/jobs/*` endpoints are **Admin-only** manual/ops triggers (with an optional `asOf` override); the timer path runs in-process with no auth.
- **Refunded(5)** is seeded but no endpoint sets it (tied to the unbuilt payment/refund flow).
- Availability is freed by Cancelled(3), Refunded(5), Rejected(6); held by Pending/Confirmed/Completed.

**Schema:** `Bookings`, `BookingStatuses` (6 seeded), `BookingGuests` (multi-guest), `BookingPayments` (scaffold only — no gateway). Migrations: `AddInitialBooking`, `RemoveBookingReviews`, `AddRejectedBookingStatus`. The `BookingReview` entity is preserved (unused) at `WeStay.BookingService/Future/`.

**⚠️ Schema anomaly:** `Bookings` has both `StatusId` and a redundant nullable `BookingStatusId` (EF shadow FK from the `BookingStatus.Bookings` nav not being wired to `StatusId`). Harmless; reconcile with an explicit relationship config + migration.

---

## 4. MessagingService (`WeStayMessaging`)

All `[Authorize]` except file download. `ConversationsController` (9 endpoints), `MessagesController` (8), `FileUploadController` (2: upload auth, download anon), **SignalR hub** `/messagehub` (real-time, typing indicators, read receipts). Conversations carry optional `ListingId`/`BookingId` (inquiry threads). Messages support file/image attachments.

**Stubs:** the in-process `NotificationServices` delegates Email/SMS to NotificationService over HTTP, but Push and Broadcast return `false` (not wired); `ConversationService` user-name enrichment was previously mock (not re-verified). File upload writes to local `wwwroot/uploads` (disk, unlike ListingService's Azure Blob).

**Schema:** `Conversations`, `ConversationTypes` (4 seeded), `ConversationParticipants`, `Messages`, `MessageReads`. Migration: `InitialMessagingSchema`.

---

## 5. NotificationService (`WeStayNotification`)

`SMSController` (send auth, send-verification anon, validate/format-phone auth) + internal `NotificationsController` (`/api/notifications/email`, `/sms` — `[ServiceAuth]` internal-key-gated, service-to-service, **not** gateway-routed). Email (SendGrid + SMTP) and SMS (Twilio) work; a background processor polls pending notifications every 60s. **Stub:** Push (FCM) send-code exists but `GetUserFcmTokensAsync` returns empty (no device-token store) → push can't deliver.

**Event-driven notifications (wired):** 9 business events now trigger Email/SMS here via direct HTTP — BookingService (created→host SMS+Email, confirmed→guest SMS+Email, rejected→guest, cancelled→other party, auto-cancelled→guest), AuthService (registration→welcome email), ReviewService (review posted→host). Each producing service has its own `NotificationClient` (mirrors `MessagingService/Services/NotificationServices.cs`) and dispatches **fire-and-forget on a background `Task` with its own DI scope** (`IServiceScopeFactory`) — a slow/unavailable NotificationService adds **zero latency** and can never fail the triggering operation. Recipient contact is resolved via `GET /api/auth/users/{id}/contact`; message **content** stays within each producer's own data (booking code / ids / dates), no cross-service enrichment. Note: `SendEmailRequest.TextContent` is effectively required (non-nullable), so each `NotificationClient` sends a plaintext fallback derived from the HTML. No event bus — direct HTTP is the Phase 1 choice.

**Schema:** `Notifications`, `NotificationTypes` (8 seeded), `NotificationTemplates` (with `Language` — i18n-ready), `UserNotificationPreferences` (per-channel opt-in). Migration: `InitialNotificationSchema`.

---

## 6. ReviewService (`WeStayReview`) — NEW

Reviews and star ratings. Only the **guest of a Completed booking** may review, **once per booking** (unique index on `BookingId`).

**Endpoints** — `ReviewsController`:

| Method | Route | Auth |
|---|---|---|
| POST | `/api/reviews` | Authenticated (guest of a Completed booking) |
| GET | `/api/reviews/listing/{listingId}` | Anonymous (paginated) |
| GET | `/api/reviews/listing/{listingId}/summary` | Anonymous (avg rating + count) |
| GET | `/api/reviews/user/{userId}` | Authenticated (self or Admin) |
| PUT | `/api/reviews/{id}` | Authenticated (original reviewer, 30-day edit window) |
| DELETE | `/api/reviews/{id}` | Authenticated (reviewer or Admin) |

Create validates over HTTP (`GET /api/bookings/{id}/info`): booking exists + **Completed** → else 400; caller is the booking's guest → else 403; no existing review → else **409**. `ListingId` is derived from the booking (not trusted from the client). After each create/update/delete, ReviewService recomputes the listing's aggregates and calls `PUT /api/listings/{id}/rating` (best-effort; logged on failure) so search can sort by rating cheaply — the summary endpoint remains the live source of truth.

**Schema:** `Reviews` (ListingId, BookingId [unique], ReviewerId, Rating 1–5, Comment, **HostReply/HostReplyAt nullable** — columns ready, host-reply endpoint is Phase 3.5, CreatedAt/UpdatedAt). Migration: `InitialReviewSchema`.

---

## 7. ApiGateway (Ocelot, no database)

`ocelot.json` is the single source of truth; JWT validated at the gateway on protected routes (unified `Bearer` scheme). Routes: `/api/auth/users/*` (**Admin** via `RouteClaimsRequirement`), `/api/auth/*` (open), `/api/listings/*` (GET open / writes Bearer), `/api/search/*` (open), `/api/bookings/*` (GET open / writes Bearer), `/api/conversations/*` + `/api/messages/*` (Bearer), `/api/fileupload/*` (open), `/api/sms/*` (open), `/api/reviews/*` (GET open / writes Bearer), `/health`. Internal `/api/notifications/*` is intentionally not routed.

---

## Phase 1 scope vs. actual state

| # | Phase 1 item | Status | Notes |
|---|---|---|---|
| 1 | Owner onboarding + listing (short-term/long-term/sale) | **Done** | `become-host` + listing CRUD; `Category` = ShortTerm/LongTerm/Sale. |
| 2 | Photo uploads | **Done** | Real `IFormFile` → Azure Blob. |
| 3 | Search with filters | **Done** | Location, price, capacity, type, category, amenities, pagination, sort (incl. working rating sort). |
| 4 | Map view support | **Done** | Lat/Long returned; bounding-box filter. |
| 5 | Short-term booking + availability calendar | **Done** | Create (+capacity validation), date-range calendar, and the **full lifecycle** (confirm/reject/cancel/complete) all exist. |
| 6 | JazzCash payment | **Not Started** | No gateway code; only a `BookingPayments` DB scaffold. |
| 7 | Inquiry/messaging (long-term & sale) | **Done\*** | Conversations/messages/real-time/attachments, linked to listings/bookings. *Caveat: new-message notifications not wired.* |
| 8 | Email + SMS notifications | **Done** | Sending (SendGrid/SMTP + Twilio) wired to **9 app events** (booking/auth/review) via direct HTTP, fire-and-forget background dispatch. Push still stubbed (no device-token store); OTP still on direct provider SDKs (tracked). |
| 9 | Featured/boosted listings | **Done** | `IsFeatured`/`FeaturedUntil`, real featured query, Host/Admin toggle. (No paid-boost flow — as scoped.) |
| 10 | Admin capabilities | **Done** | Role system + Admin-gated `set-role`/gateway enforcement, plus admin *operations*: KYC moderation (list/approve/reject), listing moderation (force-deactivate/reactivate + all-listings view, owner-ban-override blocked), and the all-bookings overview. |

**Beyond Phase 1 (built):** Reviews & star ratings (Phase 3, this service), ID verification (KYC), OTP (phone/email), SignalR chat + attachments, notification preferences + i18n templating, multi-guest bookings, discovery endpoints (`similar`/`popular-locations`/`stats`), booking confirm/reject/complete lifecycle.

---

## Service-to-service auth (built)

The internal/service-to-service endpoints are protected by a shared static API key (a "service token"), enforced by a per-service `[ServiceAuth]` attribute (`Security/ServiceAuthAttribute.cs`) that compares header `X-Internal-Api-Key` to config `ServiceAuth:InternalApiKey` (constant-time; **fail-closed** — 401 on missing/invalid, 500 if the server has none configured). **8 endpoints gated:** ListingService `/price`, `/capacity`, `/owner`, `/rating`; BookingService `/info`; AuthService `/api/auth/users/{id}/contact`; NotificationService `/api/notifications/email`, `/sms`. They stay `[AllowAnonymous]` (no user JWT) but require the key, so they're callable **service-to-service only** — not via the gateway with a normal user JWT (the wildcard-routed ones return 401; `/api/notifications/*` isn't routed at all). Callers attach the header automatically (the `NotificationClient`s + the cross-service `HttpClient`s in Booking/Review/Auth). The key lives in **User Secrets across all 6 services** that call/host these — Auth, Listing, Booking, Notification, Review, **and MessagingService** (wired for forward-compat, though its notification path isn't firing yet). Dev-default `westay-dev-internal-key-change-in-prod`; production must override `ServiceAuth:InternalApiKey` (fail-closed if unset).

---

## Quality / testing

- **Integration tests:** `WeStay.Tests` (xUnit, HTTP-level via the gateway) — **44/44 passing**. Covers auth chain, roles, listing CRUD + category + map fields, price/capacity cross-service calls, booking flow + capacity, booking confirm/reject (+ ownership/status guards), the auto-complete / auto-cancel jobs (incl. Admin-only enforcement → 403 for non-admins), admin moderation (KYC approve/reject, listing deactivate/reactivate + owner-ban-override block, all-bookings overview, non-admin → 403), **service-to-service auth (each of the 7 internal endpoints → 401 without the key)**, and reviews (eligibility, ownership, double-review, summary average). **Now needs all six services running** (Auth, Listing, Booking, Review, **Notification**, Gateway) — NotificationService became required for the `/api/notifications/email`+`/sms` rejection tests (called directly on port 7284). Tests attach the internal key (`TestConfig.InternalApiKey`, matching the services' `ServiceAuth:InternalApiKey`) when calling internal endpoints. Runs **serially** (`DisableTestParallelization`) because the job tests trigger global batch sweeps.
- **Migrations:** all six data services on their own databases; `EnsureCreated()` removed.
- **Secrets:** none committed; read from User Secrets / environment variables. `appsettings.json` are placeholder-only; `appsettings.Development.json`/`Production.json` git-ignored.

---

## Recommended next priorities

1. **JazzCash payment (#6)** — the only Not-Started Phase 1 item; fills the `BookingPayments` scaffold (initiate + webhook) and enables Refunded.
2. **Admin operations (#10)** — moderation, verification approval, bookings overview on top of the role system.
3. **Schema cleanup** — remove the redundant `Bookings.BookingStatusId` shadow FK; implement the FCM device-token store for push.

*(Done since the last revision: **service-to-service auth** — the internal endpoints are now `[ServiceAuth]`-key-gated, no longer relying on network trust. See "Service-to-service auth (built)" above.)*

**Tracked (not urgent):** OTP phone/email still send via direct Twilio/SendGrid SDK calls in AuthService (`PhoneVerificationService` / `EmailService`), bypassing NotificationService — migrate to a `NotificationClient` call to unify with the other 9 event notifications. A real **message bus** would later replace the direct-HTTP + background-dispatch approach for all cross-service events.
