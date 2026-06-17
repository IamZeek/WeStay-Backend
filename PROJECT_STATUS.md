# WeStay Backend — Project Status & Audit Report

**Generated:** 2026-06-17
**Scope:** Full-solution audit of `WeStay.sln` (8 projects, .NET 8)
**Branch:** `master`

> This report is a point-in-time audit. File paths and line numbers reference the state of the repo at generation time. Every finding cites a file so it is actionable.

---

## 0. Executive Summary

WeStay is an 8-project .NET 8 microservices solution for a property-rental/marketplace platform (short-term stays, long-term rentals, sales). Four services are substantially built (Auth, Listing, Booking, Messaging, Notification), one is a default template (Review), and the shared library (Common) is empty.

**The single most important finding:** the system **cannot work end-to-end as wired today**. The API Gateway validates JWTs with a different signing key, issuer, and audience than the AuthService uses to sign them, and the Gateway's Ocelot routes point at ports that no service actually listens on. So even though individual services are well-built, a request cannot flow Client → Gateway → Service successfully.

**Health by area:**

| Area | State |
|------|-------|
| Individual service code quality | Good (consistent async, validation, logging in most) |
| Service-to-service integration | **Broken** (JWT mismatch, wrong ports, no payment service) |
| Tests | **Zero across all 8 projects** |
| Infrastructure (Docker/CI/CD) | **None** |
| Secrets management | **Poor — real OAuth secret committed to git** |
| Phase 1 feature coverage | ~40% (payment, listing-type taxonomy, photo upload, SMS/notification wiring all missing) |

---

## 1. Architecture Overview

### 1.1 Projects in the solution

| Project | Purpose / Domain | State | Persistence |
|---------|------------------|-------|-------------|
| `WeStay.ApiGateway` | Ocelot edge gateway, JWT validation, CORS, routing | **Partially built — misconfigured** | n/a |
| `WeStay.AuthService` | Users, auth, JWT issuance, OAuth, OTP/verification | **Feature-complete** (1 logic bug) | EF migrations |
| `WeStay.ListingService` | Listings, search, amenities, images **+ a full booking impl** | **Feature-complete** | EF migrations |
| `WeStay.BookingService` | Bookings, availability, payments, reviews (standalone) | **Built but orphaned** | EF migrations |
| `WeStay.MessagingService` | Conversations, messages, SignalR real-time, file upload | **Feature-complete** (notif sending stubbed) | `EnsureCreated()` — no migrations |
| `WeStay.NotificationService` | Email (SendGrid/SMTP), SMS (Twilio), Push (FCM), templates | **Mostly built** (FCM token store stubbed) | `EnsureCreated()` — no migrations |
| `WeStay.ReviewService` | (intended) reviews | **Scaffold only** — default `weatherforecast` template | None |
| `WeStay.Common` | (intended) shared DTOs/utilities | **Empty** — single empty `Class1.cs` | None |

There is **no `WeStay.PaymentService`** project, yet the Gateway (`ocelot` route to port 7004) and BookingService (`appsettings.json` → `Services:PaymentService: https://localhost:7004`) both reference one. Payment is referenced but does not exist.

### 1.2 How services communicate

- **Client → Gateway:** REST over HTTPS via Ocelot (`WeStay.ApiGateway/ocelot.json`).
- **Service → Service:** direct synchronous HTTP with `HttpClient`:
  - `WeStay.BookingService/Services/BookingService.cs:159` — calls `GET {ListingService}/api/listings/{id}/price`.
  - `WeStay.NotificationService/Services/NotificationServices.cs:501,524,547` — calls AuthService to fetch user email/phone/name.
  - `WeStay.MessagingService/Services/ConversationService.cs:145` — *intended* to call AuthService for user info, but **returns mock data** (`User{id}@example.com`) instead.
- **Real-time:** SignalR hub `WeStay.MessagingService/Hubs/MessageHub.cs` (functional).
- **Message bus / events:** **None.** `appsettings.json` files for Messaging/Notification contain `AzureServiceBus` and `RabbitMQ` config sections, but **no consumer or publisher code exists** for either. All inter-service calls are direct HTTP.

### 1.3 API Gateway setup

`WeStay.ApiGateway` uses **Ocelot 24.0.1**. Three serious problems:

1. **Conflicting route definitions.** Routes are defined in **two places** that disagree:
   - `ocelot.json` (explicitly loaded at `Program.cs:70`) — routes `/api/v1/auth → 7005`, `/api/v1/public → 7006`, `/api/v1/messaging → 7001`, `/api/listings|bookings|search → 7002`.
   - `appsettings.json` `"Ocelot"` section (lines 19–94) — routes `/api/v1/messaging → 7001`, `/api/v1/booking → 7002`, `/api/v1/property → 7003`, `/api/v1/payment → 7004`.
   These use different URL conventions and different downstream ports. The `appsettings.json` `Ocelot` block, the `RateLimiting` block (lines 96–113), and the `Serilog` block (lines 119–143) are **dead config** — `Program.cs` never wires up Serilog or Ocelot rate-limiting.

2. **Downstream ports are wrong.** Actual HTTPS ports from each `Properties/launchSettings.json`:
   | Service | Actual HTTPS port | Gateway routes to |
   |---------|------------------|-------------------|
   | AuthService | **7019** | 7005 (auth) ❌ |
   | ListingService | **7002** | 7002 ✓ |
   | BookingService | **7292** | 7002 (via ListingService) ❌ |
   | MessagingService | **7179** | 7001 ❌ |
   | NotificationService | **7284** | (not routed) |
   | ReviewService | **7084** | (not routed) |
   Only ListingService's port is correct. The Gateway points auth/messaging at ports nothing listens on.

3. **The custom `AuthenticationMiddleware` is ineffective.** `WeStay.ApiGateway/AuthenticationMiddleware.cs:30` gates on `endpoint?.Metadata.GetMetadata<AuthorizeAttribute>()`, but Ocelot-routed requests have no MVC endpoint metadata, so `GetEndpoint()` returns null and the middleware never enforces anything. Ocelot's own `AuthenticationOptions` is what actually enforces auth.

---

## 2. Feature Completeness (per service)

### 2.1 AuthService — `WeStay.AuthService`
**Status: feature-complete.** All 11 endpoints in `Controllers/AuthController.cs` are implemented.

| Endpoint | Method | Auth | Notes |
|----------|--------|------|-------|
| `/api/auth/register` | POST | anon | BCrypt hashing, issues JWT |
| `/api/auth/login` | POST | anon | checks `IsActive` |
| `/api/auth/external-login` + callback | GET | anon | Google/Facebook OAuth |
| `/api/auth/profile` | GET | **[Authorize]** | |
| `/api/auth/change-password` | POST | **[Authorize]** | |
| `/api/auth/verification-update` | PUT | **[Authorize]** | ID-document verification |
| `/api/auth/send-otp-phone` / `verify-otp-phone` | POST | **[Authorize]** | Twilio SMS OTP |
| `/api/auth/send-otp-email` / `verify-otp-email` | POST | **[Authorize]** | SendGrid email OTP |

**Bugs / gaps:**
- **Inverted logic bug:** `Services/UserService.cs:64` — `UpdateUserStatusAsync` reads `if (user != null) return false;` (should be `== null`). This means **phone/email verification status never persists**. High priority.
- OTP for phone is stored in a **non-thread-safe in-memory `Dictionary` with no TTL** (`Services/PhoneVerificationService.cs`), lost on restart. Email OTP uses `IMemoryCache` with 5-min TTL (acceptable).
- `VerifyOtpPhone` accepts a `dynamic` request (`AuthController.cs:425`) — no type safety/validation.
- **No role system.** No `Role`/`UserType` field on `User`. The Gateway defines `AdminOnly`/`HostOnly`/`GuestOnly` policies (`Program.cs:59–66`) and JWTs carry no role claim — so those policies can never be satisfied. Admin capabilities (Phase 1) are blocked on this.

### 2.2 ListingService — `WeStay.ListingService`
**Status: feature-complete for listings & search.** Endpoints across `ListingsController.cs`, `SearchController.cs`, `BookingsController.cs` all implemented.

- **Listings CRUD:** GET (anon for single), POST/PUT/DELETE/PATCH-status (all `[Authorize]`). Delete is **soft** (sets `Status = Inactive`).
- **Search (`SearchController.cs`, all anon):** `/api/search` supports location (city/state/country/address), price range, guests, bedrooms, beds, bathrooms, type, amenities, pagination, sort. Plus `featured`, `similar/{id}`, `popular-locations`, `stats`.
- **Incomplete:** `Services/SearchService.cs:101` — **rating sort is a no-op placeholder** (`OrderByDescending(l => 0)`). `GetFeaturedListingsAsync` uses `OrderBy(l => Guid.NewGuid())` (`SearchService.cs:125`) — full-table random sort, inefficient.

### 2.3 BookingService — `WeStay.BookingService`
**Status: built but orphaned (not reachable through the Gateway).** 6 endpoints in `Controllers/BookingsController.cs`, all implemented, all `[Authorize]` except `POST /availability` (`[AllowAnonymous]`).

- Create / get-by-id / get-by-code / get-by-user / check-availability / cancel — all functional.
- **No payment endpoint and no confirm endpoint exposed via the controller** (the service has `ConfirmBookingAsync` but no controller route).
- Gateway routes `/api/bookings/*` to **port 7002 (ListingService)**, not 7292 (this service). This standalone service is **dead-routed**.

### 2.4 MessagingService — `WeStay.MessagingService`
**Status: feature-complete except notification sending.** 10 conversation endpoints, 8 message endpoints, 2 file-upload endpoints, all `[Authorize]` (file download is anon). SignalR hub functional.
- **`Services/NotificationServices.cs` is fully stubbed** — `SendEmailAsync`/`SendSmsAsync`/`SendPushNotificationAsync` log and return `true` without sending. This **duplicates** NotificationService's purpose.
- `Services/ConversationService.cs:145 GetUserInfoAsync` returns **mock user data**, not a real AuthService call.

### 2.5 NotificationService — `WeStay.NotificationService`
**Status: mostly built.** Only one controller exists: `Controller/SMSController.cs` (4 SMS endpoints). Email/Push/Notification have **no controllers** — they're invoked internally or via the background worker.
- Email (SendGrid + SMTP fallback), SMS (Twilio), Push (FCM) services implemented.
- Background worker `Services/NotificationProcessorService.cs` (`BackgroundService`, 1-min poll) processes pending notifications. Functional.
- **Stubs:** `PushNotificationService.cs:292 GetUserFcmTokensAsync` returns empty list (no device-token store), so **push can never actually deliver to a user**. `NotificationServices.cs:590 DeleteNotificationAsync` is a no-op (no `IsDeleted` field).

### 2.6 ReviewService — `WeStay.ReviewService`
**Status: scaffold only.** `Program.cs` is the unmodified `weatherforecast` template. No controllers, no DbContext, no models. **0% built.** Note: BookingService already contains a `BookingReview` entity + `BookingReviewRepository`, so review functionality is half-implemented *in the wrong service*.

### 2.7 Common — `WeStay.Common`
**Status: empty.** Single empty `Class1.cs`. No project references it. All cross-cutting concerns (JWT setup, DTOs, error handling, audit base classes) are **duplicated** in each service instead of shared here.

---

## 3. Database State

### 3.1 Databases / schemas

Every service points its connection string at a database literally named **`WeStay`** (or `WeStayMessaging`), on **hardcoded machine names** that differ per service:
- AuthService → `Server=DEVB;Database=WeStay` (`appsettings.json:3`)
- ListingService → `Server=DEVB;Database=WeStay` (`appsettings.json:3`)
- BookingService → `Server=DESKTOP-I0V8MIM;Database=WeStay` (`appsettings.json:3`)
- NotificationService → `Server=localhost;Database=WeStay`
- MessagingService → `Server=localhost;Database=WeStayMessaging` **but** `Program.cs:18` reads connection-string key `"MessagingConnection"` while `appsettings.json` provides `"WeStayDatabase"` — **key mismatch → null connection string → startup failure.**

**Problem:** Auth, Listing, and Booking all target the **same `WeStay` database** but each `EnsureCreated`/migrates its own tables into it — there is no schema-per-service isolation, which defeats the microservice data-ownership principle. Machine-specific server names (`DEVB`, `DESKTOP-I0V8MIM`) are committed and non-portable.

### 3.2 Migrations vs EnsureCreated (inconsistent)

| Service | Strategy | Migration |
|---------|----------|-----------|
| AuthService | EF migrations | `20250823201358_AddInitialMigrtion` (note typo "Migrtion") |
| ListingService | EF migrations | `20250823200938_AddInitialListingSchema` |
| BookingService | EF migrations | `20250824003951_AddInitialBooking` (seeds 5 statuses) |
| MessagingService | **`EnsureCreated()`** (`Program.cs:160`) | **none** |
| NotificationService | **`EnsureCreated()`** | **none** |
| ReviewService | none | n/a |

`EnsureCreated()` and migrations **cannot coexist on the same database** — and Messaging/Notification can never evolve their schema without dropping the DB. Standardize on migrations.

### 3.3 Audit fields, soft-delete, indexing

| Service | CreatedAt/UpdatedAt | Soft-delete | Indexes |
|---------|--------------------|-------------|---------|
| Auth | ✓ (User, Verification) | ✗ (`IsActive` exists but not query-filtered) | UserId (unique), DocumentNumber, Status |
| Listing | ✓ (Listing, Booking) | partial (Status enum, not `IsDeleted`) | HostId, Status, (City,Country). **No index on PricePerNight or Type** despite being primary search filters |
| Booking | ✓ (all entities) | ✗ (hard delete) | UserId, ListingId, StatusId, CheckIn, CheckOut, BookingCode(unique), PaymentIntentId |
| Messaging | ✓ (Message has IsEdited/IsDeleted + timestamps) | ✓ (Message.IsDeleted, Participant.IsActive) | ConversationGuid(unique), UpdatedAt, (Conv,User) unique, (Conv,CreatedAt) |
| Notification | ✓ | ✗ (DeleteNotification is a no-op; no IsDeleted field) | UserId, TypeId, IsSent, Channel, CreatedAt |

**Missing audit field:** no "who" tracking (CreatedBy/UpdatedBy) on any entity. **Migration bug:** `WeStay.AuthService/Migrations/20250823201358_AddInitialMigrtion.cs:76` declares `RejectionReason` as `nullable: false` though the model treats it as optional.

---

## 4. Code Quality Issues

### 4.1 Secrets / connection strings in source control
**All 7 `appsettings.json` files are committed to git** (confirmed via `git ls-files`). The critical one:

- **`WeStay.AuthService/appsettings.json:12-13` — REAL, LIVE Google OAuth credentials committed:**
  - `ClientId: "81810474302-bb8053oth4sc5faatem939a03eplaufa.apps.googleusercontent.com"`
  - `ClientSecret: "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"`
  **These must be rotated immediately and removed from history.**
- Hardcoded JWT **fallback keys** baked into every service's `Program.cs` (used silently if config is missing/short):
  - AuthService `Program.cs:32` → `"DefaultDevelopmentKey-32-characters-long!"`
  - BookingService `Program.cs:21` → `"BookingServiceKey-32-characters-long-!"`
  - ListingService `Program.cs:27` → `"ListingServiceKey-32-characters-long-here!"`
  - MessagingService `Program.cs:35` → `"MessagingServiceKey-32-characters-long-!"`
  - NotificationService `Program.cs:27` → `"NotificationServiceKey-32-characters-long-!"`
- Hardcoded DB server names (`DEVB`, `DESKTOP-I0V8MIM`) in connection strings.

### 4.2 The JWT key/issuer/audience mismatch (system-breaking)
This is the headline integration bug:

| Component | Signing/validation key | Issuer | Audience |
|-----------|------------------------|--------|----------|
| **AuthService issues** (`appsettings.json:6` + `JwtTokenGenerator.cs`) | `Jwt:Key` = `"YourSuperSecretKeyHereAtLeast32CharactersLong"` | `WeStay` | `WeStayUsers` |
| **Gateway validates** (`appsettings.json:13`) | `JwtSettings:Secret` = `"your-super-secret-jwt-key-minimum-32-characters-long"` | `WeStay.ApiGateway` | `WeStay-Services` |

The Gateway validates with `ValidateIssuer/Audience/IssuerSigningKey = true` (`Program.cs:28-34`) against **different values than AuthService stamps**. **Every authenticated request through the Gateway will 401.** The downstream services (Listing/Booking/Messaging) each define their *own* `Jwt:Key` too, none guaranteed to match AuthService. There is no shared signing secret.

### 4.3 Inconsistent patterns across services
- **Repository pattern is used inconsistently:** Booking, Messaging, Notification use repositories + interfaces. **Auth and Listing access `DbContext` directly from service classes** (e.g., `WeStay.AuthService/Services/AuthService.cs`, `WeStay.ListingService/Services/ListingService.cs`). Pick one.
- **Booking is implemented twice:** `WeStay.ListingService/Services/BookingService.cs` (with its own `Booking` entity in `ListingDbContext`) AND the entire `WeStay.BookingService`. Two booking tables, two availability implementations, divergent status models. This is the worst duplication in the codebase.
- **Notification sending implemented twice:** `WeStay.MessagingService/Services/NotificationServices.cs` (stubbed) overlaps `WeStay.NotificationService` entirely.
- **CORS:** Gateway uses a proper origin allow-list; Listing and Messaging use `AllowAnyOrigin()` (Listing `Program.cs:50`) / `AllowAnyOrigin()+AllowCredentials()` (Messaging `Program.cs:122`, which is an invalid combination ASP.NET will reject at runtime).

### 4.4 Error handling & logging
Generally good: controllers wrap actions in try/catch, use `ILogger`, map domain exceptions to status codes. Issues:
- `WeStay.AuthService/Services/UserService.cs:39` — `CreateUserAsync` swallows all exceptions and returns `null`.
- `AuthController.cs:141` returns **400 for `UnauthorizedAccessException`** (should be 401).
- No global exception-handling middleware in any service; each controller repeats try/catch.

### 4.5 Missing input validation
- Booking does **not validate `NumberOfGuests` against listing capacity** before creating a booking (`WeStay.BookingService/Services/BookingService.cs`).
- No password-complexity rules beyond `[MinLength(6)]` (AuthService).
- File download endpoint `WeStay.MessagingService/Controllers/FileUploadController.cs:115` is `[AllowAnonymous]` and takes a raw `filename` — **path-traversal risk**; no sanitization confirmed.
- Unsafe claim parsing: `int.Parse(User.FindFirst(...)?.Value)` in Messaging controllers (`ConversationsController.cs:283`, `MessagesController.cs:283`, `FileUploadController.cs:165`, `MessageHub.cs:126`) throws `NullReferenceException`/`FormatException` if the claim is absent.

### 4.6 Async / performance
- Async usage is good across all services (all DB calls awaited).
- **`app.UseOcelot().Wait()`** (`WeStay.ApiGateway/Program.cs:118`) — blocking `.Wait()` on async during startup; use `await app.UseOcelot()`.
- **Performance hotspots (not true N+1, but inefficient):**
  - `WeStay.BookingService/Services/AvailabilityService.cs:32` — loads *all* bookings for a listing then expands every date range in memory.
  - `WeStay.ListingService/Services/SearchService.cs:125` — `OrderBy(Guid.NewGuid())` random sort over the whole listings table.

---

## 5. Testing Status

**There are zero tests in the entire solution.** No test project exists in `WeStay.sln`; no `*.Tests.csproj`, no xUnit/NUnit/MSTest references, no test files in any service.

| Service | Unit tests | Integration tests |
|---------|-----------|-------------------|
| All 8 projects | **None** | **None** |

- **No integration tests for the critical flows** (booking, payment, availability, auth).
- This is the largest quality risk after the integration bugs — there is no safety net for the refactors this report recommends.

---

## 6. Infrastructure & DevOps

- **Dockerfiles:** **None** (no `Dockerfile` in any project).
- **docker-compose / orchestration:** **None.**
- **CI/CD:** **None.** No `.github/` (GitHub Actions), no `azure-pipelines.yml`, no other pipeline config.
- **README / docs:** **None** (no `.md` files in the repo before this report).
- **Config approach:** `appsettings.json` + `appsettings.Development.json` per service, committed to git (including secrets). **No environment-variable overrides, no Azure Key Vault, no User Secrets** wired up. There is no `.env`, no secrets provider.
- **Service discovery:** none — ports are hardcoded in `ocelot.json` and in each service's `Services:*` config.

---

## 7. Inter-Service Communication Issues

- **Tight coupling / shared database:** Auth, Listing, and Booking share one physical `WeStay` database. Booking also reaches into Listing over HTTP for pricing. This is a distributed monolith, not isolated microservices.
- **Synchronous chains with no resilience:** `BookingService → ListingService` (`BookingService.cs:159`) and `NotificationService → AuthService` (`NotificationServices.cs:501+`) are plain `HttpClient` calls with **no timeout override, no retry, no circuit breaker** (no Polly). If ListingService is down, booking creation hangs/fails with no graceful degradation — a cascading-failure risk.
- **No message bus despite config for one:** `AzureServiceBus` and `RabbitMQ` sections exist in Messaging/Notification `appsettings.json` but **nothing publishes or consumes**. Notifications that should be event-driven (booking confirmed → send email/SMS) are instead either direct calls or a 1-minute DB poll.
- **Orphaned/duplicated responsibilities:** standalone `BookingService` is not routed by the Gateway; `ReviewService` is empty while reviews live in BookingService; notification logic is split between two services.

---

## 8. Gaps Against Phase 1 Requirements

| # | Phase 1 item | Status | What's missing |
|---|--------------|--------|----------------|
| 1 | Owner onboarding & property listing (short-term, long-term, **sale**) | **Partial** | Listing CRUD works, but `ListingType` enum (`Models/Listing.cs:7`) is only `Apartment/Home/Room/Farmhouse/Villa` — **there is no short-term vs long-term vs sale dimension at all.** No "owner onboarding" beyond generic user registration; no Host/Owner role. |
| 2 | Photo uploads | **Partial / Not Started** | ListingService stores **image URLs only** (`ListingImage.ImageUrl`); there is **no upload endpoint** (`IFormFile`) and no blob/storage integration. Clients must host images elsewhere. Only Messaging has a real file-upload endpoint. |
| 3 | Search with filters (city, area, price, type, bedrooms) | **Done** | `SearchController` + `SearchService` cover city/area/price/type/bedrooms/guests/amenities with pagination. (Rating sort is a stub; minor.) |
| 4 | Map view support (backend data ready) | **Partial** | `Listing.Latitude`/`Longitude` exist and persist (`Models/Listing.cs:76-77`), but they're **not returned in search responses and not filterable** (no bounding-box/radius query). |
| 5 | Short-term booking flow with availability calendar | **Partial** | Booking create + availability check exist (in **both** ListingService and BookingService), but there is **no calendar endpoint** returning an availability grid (BookingService computes unavailable dates in-memory). Guest-capacity validation missing. Duplicated implementations must be reconciled. |
| 6 | **JazzCash payment integration** | **Not Started** | **Zero JazzCash code. No PaymentService exists.** BookingService only stores payment *records* (`BookingPaymentRepository`) with a `PaymentIntentId` placeholder; nothing calls any gateway, no webhook handler. |
| 7 | Inquiry/messaging for long-term & sale listings | **Done (mostly)** | MessagingService supports conversations tied to `ListingId`/`BookingId` with a `Booking`/`Direct`/`Support`/`Group` type, SignalR real-time, read receipts. Caveat: user-info lookup is mocked (`ConversationService.cs:145`). |
| 8 | Email + SMS notifications | **Partial** | NotificationService has working SendGrid/SMTP + Twilio code, but it is **not triggered by booking/auth events** (no message bus), push delivery is stubbed (no device-token store), and the Gateway doesn't route to it. Messaging's parallel notification impl is fully stubbed. |
| 9 | Featured/boosted listings | **Not Started** | No `IsFeatured`/`Boosted`/promotion fields on `Listing`. `GetFeaturedListingsAsync` just returns **8 random listings** (`SearchService.cs:118-125`) — no monetization, no admin toggle, no expiry. |
| 10 | Admin capabilities | **Not Started** | No admin controllers, no admin endpoints, and **no role/claim system** to authorize them. Gateway's `AdminOnly` policy exists but can never be satisfied (no role claim is ever issued). |

---

## 9. Prioritized Punch List — Top 10 (ordered by what unblocks the most)

1. **Unify JWT signing across the whole solution.** Put one `Jwt:Key` + `Issuer` + `Audience` in shared config and use it identically in AuthService (issuing), the Gateway (`appsettings.json:13` / `Program.cs:28-34`), and every downstream service. *Without this, nothing authenticated works end-to-end — it blocks every other authenticated feature.*
2. **Fix the Gateway routing.** Reconcile `ocelot.json` vs the dead `Ocelot` block in `appsettings.json` into one source of truth, and point each route at the **correct** HTTPS port (Auth 7019, Listing 7002, Booking 7292, Messaging 7179, Notification 7284). Remove/standby-route the non-existent payment service. *Unblocks all client→service traffic.*
3. **Get secrets out of git and rotate the leaked Google OAuth secret** (`WeStay.AuthService/appsettings.json:12-13`). Move all secrets to environment variables / User Secrets / Key Vault, scrub git history, and remove the hardcoded JWT fallback keys in every `Program.cs`. *Security-critical.*
4. **Decide the booking ownership boundary** and delete the duplicate. Either (a) keep booking inside ListingService and retire the standalone `WeStay.BookingService`, or (b) make BookingService authoritative, remove booking from ListingService, and route `/api/bookings` to port 7292. *Eliminates the biggest architectural inconsistency and unblocks the calendar/payment work.*
5. **Build payment (JazzCash).** Stand up the missing `WeStay.PaymentService` (or a payment module in the chosen booking owner), integrate JazzCash, add an initiate-payment endpoint + webhook to flip `BookingPayment` status. *Phase 1 hard requirement; unblocks the end-to-end short-term booking flow.*
6. **Introduce a roles/claims system in AuthService** (Guest/Host/Owner/Admin) and emit a `role` claim in the JWT. *Unblocks admin capabilities, host-only listing management, and the Gateway's existing `AdminOnly`/`HostOnly` policies.*
7. **Fix the verification persistence bug** (`WeStay.AuthService/Services/UserService.cs:64`, inverted null check) and move phone-OTP storage out of the in-memory dictionary into `IMemoryCache`/Redis with TTL. *Phone/email verification is silently broken today.*
8. **Standardize persistence:** convert MessagingService and NotificationService from `EnsureCreated()` to EF migrations, fix the MessagingService connection-string key mismatch (`MessagingConnection` vs `WeStayDatabase`), and give each service its own schema/database instead of sharing `WeStay`. *Unblocks safe schema evolution and removes a startup crash.*
9. **Implement real photo upload + the listing-type taxonomy** in ListingService: add an `IFormFile` upload endpoint backed by blob storage, and add a `ListingCategory` (ShortTerm/LongTerm/Sale) dimension distinct from property type. *Two Phase 1 gaps in the service that's already most complete.*
10. **Add a test project and CI, plus event-driven notifications.** Create a `WeStay.Tests` project with integration tests for auth → booking → payment, add a GitHub Actions/Azure DevOps build+test pipeline and Dockerfiles, and wire booking/auth events to NotificationService via the already-configured RabbitMQ/Service Bus (replacing the stubbed Messaging notifications and the 1-minute poll). *Establishes the safety net and decouples the synchronous chains.*

---

### Appendix: quick reference — what's real vs stub

- **Real & working:** Auth endpoints, Listing CRUD + search, Messaging (conversations/messages/SignalR/file upload), Notification email+SMS code, Booking CRUD logic.
- **Stubbed / fake:** Messaging `NotificationServices` (all sends), Messaging `GetUserInfoAsync` (mock data), Notification `GetUserFcmTokensAsync` (empty), Notification `DeleteNotificationAsync` (no-op), Listing rating-sort.
- **Missing entirely:** PaymentService/JazzCash, ReviewService (template only), Common (empty), roles/admin, photo upload, listing-type taxonomy, featured-listing monetization, message bus, Docker, CI/CD, tests.
