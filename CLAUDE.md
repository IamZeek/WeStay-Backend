# CLAUDE.md — WeStay Backend

Project context for Claude Code. For the full, detailed status see [PROJECT_STATUS.md](PROJECT_STATUS.md).

## What this is

WeStay is a property-rental/marketplace backend (short-term stays, long-term rentals, and sales) built as **.NET 8 microservices** behind an **Ocelot API gateway**, with **SQL Server** (EF Core) and **JWT** auth. Solution: `WeStay.sln`.

## Architecture: 6 active services + gateway

| Service | Dev port (HTTPS) | Database | Owns |
|---|---|---|---|
| WeStay.ApiGateway | 7087 | — | Ocelot routing, gateway JWT validation, CORS |
| WeStay.AuthService | 7019 | `WeStayAuth` | Users, JWT issuance, roles, OAuth (Google), OTP, KYC verification |
| WeStay.ListingService | 7002 | `WeStayListing` | Listings, search, amenities, images (Azure Blob), featured, cached ratings |
| WeStay.BookingService | 7292 | `WeStayBooking` | Bookings, availability, booking lifecycle, payment scaffold |
| WeStay.MessagingService | 7179 | `WeStayMessaging` | Conversations, messages, SignalR real-time, attachments |
| WeStay.NotificationService | 7284 | `WeStayNotification` | Email (SendGrid/SMTP), SMS (Twilio), templates, preferences |
| WeStay.ReviewService | 7084 | `WeStayReview` | Reviews + star ratings |

Also: `WeStay.Common` (empty shared lib). Each data service has **its own database**.

## Conventions (follow these)

- **Migrations, not `EnsureCreated()`.** Every data service uses EF Core migrations. Generate with `dotnet ef migrations add <Name> --project WeStay.<Service>` and apply with `dotnet ef database update`. `dotnet-ef` is installed as a global tool.
- **Unified JWT.** All services + the gateway validate with the same `Jwt:Key`/Issuer (`WeStay`)/Audience (`WeStayUsers`); AuthService signs. The token carries a `role` claim (Guest/Host/Admin). The gateway reads it as the short `role` claim (`MapInboundClaims = false`).
- **Admin seed.** AuthService idempotently ensures an admin user on startup (creates it, or promotes an existing user with that email). Config: `AdminSeed:Email` (default `admin@westay.local`) and `AdminSeed:Password`. In **Development** a dev-default password (`Admin123!`) is used so the app/tests have an admin out of the box; in **other environments** the admin is seeded **only** when `AdminSeed:Password` is explicitly set — never a default-password admin in production. This is the only way to bootstrap the first Admin (since `set-role` itself requires Admin); override via User Secrets / env vars.
- **Secrets via User Secrets / env vars — never committed.** `appsettings.json` files are placeholder-only templates (empty secret values). `appsettings.Development.json` / `Production.json` are git-ignored. Each service reads its connection string + `Jwt:Key` and fails fast at startup if missing.
- **Cross-service calls are direct HTTP** (no message bus yet) using `HttpClient` + a `Services:<Name>` config URL. Pattern: small `[AllowAnonymous]` service-to-service endpoints returning bare values/small JSON (e.g. ListingService `/price`, `/capacity`, `/owner`, `/rating`; BookingService `/info`). See `BookingService.GetListingPriceAsync` as the reference.
- **Gateway routing** lives only in `WeStay.ApiGateway/ocelot.json` (single source of truth). Convention: GET routes open, writes Bearer-validated; `/api/auth/users/*` is Admin-gated via `RouteClaimsRequirement`. Internal `/api/notifications/*` is deliberately not routed.
- **Roles** are enforced at the owning service via `[Authorize(Roles="...")]`; ownership (e.g. a host owns a listing) is checked, not just role.

## Running & testing (Windows, full DB access assumed)

Run a service in Development (so User Secrets load) on its port:
```
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="https://localhost:<port>" dotnet run --project WeStay.<Service> --no-launch-profile
```
Build all: `dotnet build WeStay.sln`.

**Integration tests:** `WeStay.Tests` — xUnit, **HTTP-level through the gateway** (mirrors curl/Postman; no in-memory hosting). Run with `dotnet test WeStay.Tests`. It needs the relevant services running (Auth, Listing, Booking, Review, Gateway for the full set) and a trusted dev HTTPS cert (`dotnet dev-certs https --trust`, also required for cross-service HTTPS). Currently **36/36 passing**. The suite runs **serially** (`DisableTestParallelization`) because the booking-job tests trigger global batch sweeps; the admin-gated job tests log in as the seeded admin (see below).

## Booking state machine

`Pending → Confirmed (host) → Completed`, with `Rejected (host)` and `Cancelled (guest/admin)` as off-ramps from Pending. `Completed` gates reviews and is reached **automatically** by the `BookingCompletionService` background job once `CheckOutDate` passes (or manually via `POST /api/bookings/{id}/complete`). A second job, `BookingExpiryService`, **auto-cancels** Pending bookings the host never confirmed within the window. Both are BookingService hosted services polling every `Booking:AutoJobIntervalMinutes` (default 60); the auto-cancel window is `Booking:AutoCancelPendingHours` (default 24). `POST /api/bookings/jobs/auto-complete` and `/jobs/auto-cancel` are **Admin-only** manual/ops triggers (optional `asOf` override); the timer path runs in-process with no auth. Statuses seeded: Pending, Confirmed, Cancelled, Completed, Refunded, Rejected.

## Phase 1 status (summary)

Done: owner onboarding + listing taxonomy, photo upload (Azure Blob), search + filters, map data, booking + availability calendar + full lifecycle, featured listings. Partial: email/SMS (sending works but not event-triggered), admin (role system exists, no admin ops endpoints). **Not started: JazzCash payment.** Beyond Phase 1, reviews & ratings are built.

## Explicitly NOT built yet

- **JazzCash / any payment gateway** — `BookingPayments` is a DB scaffold only; `Refunded` status is unreachable.
- **Frontend** — backend only; no web/mobile client.
- **Admin moderation endpoints** — no user/listing moderation, verification-approval endpoint, or all-bookings view (only `set-role` exists).
- **Service-to-service auth tokens** — internal `[AllowAnonymous]` endpoints (`/price`, `/capacity`, `/owner`, `/info`, `/rating`, `/notifications/*`) rely on network trust; they need a service token / network restriction before production.
- **Message bus / event-driven notifications** — cross-service is direct HTTP; notifications aren't fired on booking/auth events.
- **Push notifications** — FCM send-code exists but there's no device-token store.

## Known cleanups

- `Bookings.BookingStatusId` is a redundant EF shadow FK (alongside `StatusId`) — reconcile with explicit relationship config + migration.
- MessagingService push/broadcast return `false` (not wired); user-name enrichment was mock.
