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
- **Cross-service calls are direct HTTP** (no message bus yet) using `HttpClient` + a `Services:<Name>` config URL. Pattern: small service-to-service endpoints returning bare values/small JSON (e.g. ListingService `/price`, `/capacity`, `/owner`, `/rating`; BookingService `/info`) — each `[AllowAnonymous]` (no user JWT) **and** `[ServiceAuth]`-gated (shared internal key — see "Service-to-service auth"). See `BookingService.GetListingPriceAsync` as the reference.
- **Service-to-service auth.** The internal endpoints are protected by a shared static API key via a per-service `[ServiceAuth]` attribute (`Security/ServiceAuthAttribute.cs`) that checks header `X-Internal-Api-Key` against config `ServiceAuth:InternalApiKey` (constant-time compare, **fail-closed**: rejects 401 if no/invalid key, 500 if the server has none configured). Callers attach it automatically (the `NotificationClient`s + the price/capacity/owner/info/contact/rating `HttpClient`s). **8 endpoints gated:** ListingService `/price`,`/capacity`,`/owner`,`/rating`; BookingService `/info`; AuthService `/api/auth/users/{id}/contact`; NotificationService `/api/notifications/email`,`/sms`. `/api/notifications/*` stays un-routed at the gateway; the others ride wildcard routes but 401 without the key (not reachable with a normal user JWT). The key lives in User Secrets across all **6** services that call/host these (Auth, Listing, Booking, Notification, Review + MessagingService, wired for forward-compat though its notification path isn't firing yet). Dev-default `westay-dev-internal-key-change-in-prod`; override `ServiceAuth:InternalApiKey` in production.
- **Gateway routing** lives only in `WeStay.ApiGateway/ocelot.json` (single source of truth). Convention: GET routes open, writes Bearer-validated; `/api/auth/users/*` and `/api/auth/admin/*` are Admin-gated via `RouteClaimsRequirement`. Internal `/api/notifications/*` is deliberately not routed.
- **Roles** are enforced at the owning service via `[Authorize(Roles="...")]`; ownership (e.g. a host owns a listing) is checked, not just role.

## Running & testing (Windows, full DB access assumed)

Run a service in Development (so User Secrets load) on its port:
```
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="https://localhost:<port>" dotnet run --project WeStay.<Service> --no-launch-profile
```
Build all: `dotnet build WeStay.sln`.

**Integration tests:** `WeStay.Tests` — xUnit, **HTTP-level through the gateway** (mirrors curl/Postman; no in-memory hosting). Run with `dotnet test WeStay.Tests`. It now needs **all six services running** (Auth, Listing, Booking, Review, **Notification**, Gateway) — NotificationService became required for the service-auth rejection tests, which call `/api/notifications/email`+`/sms` directly on port 7284 expecting 401 — plus a trusted dev HTTPS cert (`dotnet dev-certs https --trust`, also required for cross-service HTTPS). Tests attach the internal service key (`TestConfig.InternalApiKey`, matching the services' `ServiceAuth:InternalApiKey`) when calling internal endpoints. Currently **44/44 passing**. The suite runs **serially** (`DisableTestParallelization`) because the booking-job tests trigger global batch sweeps; the admin-gated job tests log in as the seeded admin (see below).

## Booking state machine

`Pending → Confirmed (host) → Completed`, with `Rejected (host)` and `Cancelled (guest/admin)` as off-ramps from Pending. `Completed` gates reviews and is reached **automatically** by the `BookingCompletionService` background job once `CheckOutDate` passes (or manually via `POST /api/bookings/{id}/complete`). A second job, `BookingExpiryService`, **auto-cancels** Pending bookings the host never confirmed within the window. Both are BookingService hosted services polling every `Booking:AutoJobIntervalMinutes` (default 60); the auto-cancel window is `Booking:AutoCancelPendingHours` (default 24). `POST /api/bookings/jobs/auto-complete` and `/jobs/auto-cancel` are **Admin-only** manual/ops triggers (optional `asOf` override); the timer path runs in-process with no auth. Statuses seeded: Pending, Confirmed, Cancelled, Completed, Refunded, Rejected.

## Phase 1 status (summary)

Done: owner onboarding + listing taxonomy, photo upload (Azure Blob), search + filters, map data, booking + availability calendar + full lifecycle, featured listings, **event-triggered email/SMS notifications** (9 events — see "Event-driven notifications"). Partial: admin (role system exists, no admin ops endpoints). **Not started: JazzCash payment.** Beyond Phase 1, reviews & ratings are built.

## Explicitly NOT built yet

- **JazzCash / any payment gateway** — `BookingPayments` is a DB scaffold only; `Refunded` status is unreachable.
- **Frontend** — backend only; no web/mobile client.
- **Admin moderation endpoints** — no user/listing moderation, verification-approval endpoint, or all-bookings view (only `set-role` exists).
- **Message bus** — no broker; cross-service calls *and* event notifications are direct HTTP. Notifications **are** now fired on booking/auth/review events (see below); a bus would replace the direct-HTTP + background-dispatch approach later.
- **Push notifications** — FCM send-code exists but there's no device-token store.

## Event-driven notifications

- **9 business events** fire Email/SMS via NotificationService over direct HTTP. Each producing service (Booking/Auth/Review) has its own `NotificationClient` (mirrors `MessagingService/Services/NotificationServices.cs`), reading `Services:NotificationService`.
- **Fire-and-forget:** every send is dispatched on a background `Task` with its **own DI scope** (`IServiceScopeFactory`), so a slow/unavailable NotificationService adds **zero latency** to — and can never fail — the triggering operation. (An event bus would replace this later.)
- Events: booking created→host (SMS+Email), confirmed→guest (SMS+Email), rejected→guest, cancelled→the other party, auto-cancelled→guest; registration→welcome email; review posted→host.
- Recipient contact (email/phone) is resolved via internal `GET /api/auth/users/{id}/contact`; notification **content** stays in-service (booking code / ids / dates), not cross-service enrichment, so a missing listing name can't cause a failure.
- **Known inconsistency (tracked):** OTP phone & email still send via **direct Twilio/SendGrid SDK calls inside AuthService** (`PhoneVerificationService` / `EmailService`), **not** through NotificationService. Functional, but bypasses the unified path — candidate to migrate to a `NotificationClient` call later.

## Known cleanups

- `Bookings.BookingStatusId` is a redundant EF shadow FK (alongside `StatusId`) — reconcile with explicit relationship config + migration.
- MessagingService push/broadcast return `false` (not wired); user-name enrichment was mock.
