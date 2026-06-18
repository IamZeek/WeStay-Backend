# WeStay Backend — Project Status

**Last verified:** 2026-06-18
**Basis:** Inspected directly against current source code and the live remote SQL Server (all five databases). This document **replaces** the earlier audit, which was outdated after the work completed on 2026-06-17/18.

The solution has 6 active services plus an empty shared library and an unbuilt ReviewService. Each data-owning service now has its **own dedicated database**, schema is managed by **EF Core migrations** (no `EnsureCreated()`), JWT is unified across services and the gateway, and an HTTP-level integration test suite (`WeStay.Tests`) passes 22/22.

## Database map

| Service | Database |
|---|---|
| WeStay.AuthService | `WeStayAuth` |
| WeStay.ListingService | `WeStayListing` |
| WeStay.BookingService | `WeStayBooking` |
| WeStay.MessagingService | `WeStayMessaging` |
| WeStay.NotificationService | `WeStayNotification` |
| WeStay.ApiGateway | none (routing only) |
| WeStay.ReviewService | none (not built — default template) |
| WeStay.Common | none (empty shared library) |

---

## 1. AuthService (`WeStayAuth`)

**Endpoints** (`AuthController` — no class-level `[Authorize]`; anonymous unless marked):

| Method | Route | Auth |
|---|---|---|
| POST | `/api/auth/register` | Anonymous |
| POST | `/api/auth/login` | Anonymous |
| GET | `/api/auth/external-login` | Anonymous (Google only) |
| GET | `/api/auth/external-login-callback` | Anonymous |
| GET | `/api/auth/profile` | Authenticated |
| POST | `/api/auth/change-password` | Authenticated |
| POST | `/api/auth/become-host` | Authenticated (Guest → Host self-service; returns fresh token) |
| PUT | `/api/auth/users/{id}/role` | **Admin** |
| PUT | `/api/auth/verification-update` | Authenticated |
| POST | `/api/auth/send-otp-phone` / `verify-otp-phone` | Authenticated |
| POST | `/api/auth/send-otp-email` / `verify-otp-email` | Authenticated |

**Functional status:** all endpoints implemented and working. JWT carries a `role` claim (Guest/Host/Admin). Google OAuth is **conditional** (registered only when `Authentication:Google:*` is configured); Facebook OAuth has been fully removed. No stubs.

**Known smells:** phone-OTP is stored in an in-memory dictionary (no TTL/persistence, lost on restart); `verify-otp-phone` binds a `dynamic` request body.

**Schema:** `Users` (Email, PasswordHash, names, ProfilePicture, DateOfBirth, PhoneNumber, `IsActive`, **`Role int`**, `IsEmailVerified`/`IsPhoneNoVerified`, external-login fields, CreatedAt/UpdatedAt), `ExternalLogins` (UserId, Provider, ProviderKey), `Verifications` (UserId, DocumentType, DocumentNumber, ImageUrl, Status, VerifiedAt, RejectionReason). Migrations: `AddInitialMigrtion`, `AddUserRole`.

---

## 2. ListingService (`WeStayListing`)

**Endpoints** — `ListingsController` (class `[Authorize]`):

| Method | Route | Auth |
|---|---|---|
| GET | `/api/listings` | Authenticated (host's own listings) |
| GET | `/api/listings/{id}` | Anonymous |
| GET | `/api/listings/{id}/price` | Anonymous (service-to-service) |
| GET | `/api/listings/{id}/capacity` | Anonymous (service-to-service) |
| POST | `/api/listings/upload-image` | Authenticated |
| POST | `/api/listings` | Authenticated |
| PUT | `/api/listings/{id}` | Authenticated |
| DELETE | `/api/listings/{id}` | Authenticated (soft-delete → Inactive) |
| PATCH | `/api/listings/{id}/status` | Authenticated |
| POST | `/api/listings/{id}/feature` | **Host or Admin** |

**Endpoints** — `SearchController` (all Anonymous): `GET /api/search`, `/api/search/featured`, `/api/search/similar/{listingId}`, `/api/search/popular-locations`, `/api/search/stats`.

**Functional status:** all working. Photo upload uses a real `IFormFile` endpoint backed by **Azure Blob Storage**, returning the blob URL to store as `ListingImage.ImageUrl`. Search supports location, price range, guests/bedrooms/beds/bathrooms, `type`, **`category`**, amenities, **map bounding-box** (MinLatitude/MaxLatitude/MinLongitude/MaxLongitude), pagination, and sorting. Featured listings use a **real query** on `IsFeatured` + `FeaturedUntil` (replaced the old random sort).

**Stub:** `SearchService` rating-sort is a placeholder (`OrderByDescending(l => 0)`) — no rating system exists.

**Config required:** `AzureBlobStorage:ConnectionString` (required for upload) and `AzureBlobStorage:ContainerName` (optional, defaults to `listing-images`).

**Schema:** `Listings` (HostId, Title, Description, `Type int`, **`Category int`** [ShortTerm/LongTerm/Sale], Guests/Bedrooms/Beds/Bathrooms, PricePerNight, address fields, `Latitude`/`Longitude`, `Status int`, **`IsFeatured bit`**, **`FeaturedUntil`**, CreatedAt/UpdatedAt), `ListingImages` (ImageUrl, Caption, IsPrimary, DisplayOrder), `Amenities` (10 seeded), `ListingAmenities` (join). No `Bookings` table — booking ownership has moved out of this service. Migrations: `AddInitialListingSchema`, `RemoveBookingAndAddListingFeatures`.

---

## 3. BookingService (`WeStayBooking`)

**Endpoints** — `BookingsController` (class `[Authorize]`):

| Method | Route | Auth |
|---|---|---|
| POST | `/api/bookings` | Authenticated |
| GET | `/api/bookings/{id}` | Authenticated (ownership-checked) |
| GET | `/api/bookings/code/{bookingCode}` | Authenticated |
| GET | `/api/bookings/user/{userId}` | Authenticated |
| POST | `/api/bookings/availability` | Anonymous |
| GET | `/api/bookings/availability-calendar/{listingId}` | Anonymous |
| POST | `/api/bookings/{id}/cancel` | Authenticated |

**Functional status:** create (with **guest-capacity validation** fetched from ListingService over HTTP), single-date availability check, **date-range availability calendar**, and cancel all work.

**Gaps:**
- **No confirm/reject endpoint.** `ConfirmBookingAsync` exists in the service layer but has no controller route, so a booking has no API path out of `Pending` except cancellation.
- **No payment processing.** `BookingPayments` table + `BookingPaymentRepository` are a DB scaffold only — there is **zero payment-gateway / JazzCash integration** and no payment endpoint.

**Schema:** `Bookings` (BookingCode, ListingId, UserId, CheckIn/CheckOut, NumberOfGuests, TotalPrice, Currency, `StatusId`, SpecialRequests, CancellationReason, CancelledAt, CreatedAt/UpdatedAt), `BookingStatuses` (5 seeded: Pending/Confirmed/Cancelled/Completed/Refunded), `BookingGuests` (multi-guest), `BookingPayments`. Migrations: `AddInitialBooking`, `RemoveBookingReviews`.

**⚠️ Schema anomaly:** `Bookings` has both `StatusId` **and** a redundant nullable `BookingStatusId` — a leftover EF shadow foreign key created because the `BookingStatus.Bookings` navigation isn't wired to `StatusId`. Harmless but should be reconciled (explicit relationship config + migration). The `BookingReview` entity is preserved at `WeStay.BookingService/Future/` (excluded from build and DB) for a future reviews feature.

---

## 4. MessagingService (`WeStayMessaging`)

**Endpoints** — all `[Authorize]` except file download:
- `ConversationsController` (9): `GET /api/conversations`, `GET /{id}`, `GET /guid/{guid}`, `POST`, `POST /{id}/participants`, `DELETE /{id}/participants/{participantId}`, `POST /{id}/read`, `POST /{id}/archive`, `GET /{id}/unread-count`.
- `MessagesController` (8): `GET /conversation/{id}`, `GET /{id}`, `POST`, `PUT /{id}`, `DELETE /{id}` (soft-delete), `POST /{id}/read`, `POST /conversation/{id}/read-all`, `GET /conversation/{id}/count`.
- `FileUploadController` (2): `POST /api/fileupload/message/{conversationId}` (auth), `GET /api/fileupload/download/{filename}` (**Anonymous**).
- **SignalR hub** `/messagehub` — real-time delivery, typing indicators, read receipts.

**Functional status:** conversations, messages, real-time, and file attachments all work (repository-backed). Conversations carry optional `ListingId`/`BookingId`, supporting inquiry threads for long-term/sale listings and booking-related chat.

**Stubs / limitations:** the in-process `NotificationServices` now delegates Email/SMS to NotificationService over HTTP, but **Push and Broadcast return `false`** (not wired). User-name enrichment in `ConversationService` was previously mock data (not re-verified in this pass). File upload writes to local `wwwroot/uploads` (disk), unlike ListingService which uses Azure Blob.

**Schema:** `Conversations` (ConversationGuid, TypeId, **`ListingId`/`BookingId` nullable**, Title, IsArchived), `ConversationTypes` (4 seeded: Direct/Booking/Support/Group), `ConversationParticipants` (IsActive, JoinedAt, LastReadAt), `Messages` (Content, MessageType, **FileUrl/FileName/FileSize**, IsEdited/EditedAt, IsDeleted/DeletedAt), `MessageReads`. Migration: `InitialMessagingSchema`.

---

## 5. NotificationService (`WeStayNotification`)

**Endpoints:**
- `SMSController` (class `[Authorize]`): `POST /api/sms/send` (auth), `POST /api/sms/send-verification` (**Anonymous**), `POST /api/sms/validate-phone` (auth), `POST /api/sms/format-phone` (auth).
- `NotificationsController` (internal, **Anonymous**, **not** gateway-routed): `POST /api/notifications/email`, `POST /api/notifications/sms`.

**Functional status:** Email (SendGrid + SMTP fallback) and SMS (Twilio) sending are implemented and working. A `NotificationProcessorService` background worker polls pending notifications every 60s.

**Stub:** Push (Firebase/FCM) — the send code exists but `GetUserFcmTokensAsync` **returns an empty list** (no device-token store), so push cannot actually deliver to a user.

**Security note:** the internal `/api/notifications/*` endpoints are `[AllowAnonymous]` (no service-auth mechanism yet) and are deliberately **not** exposed through the gateway. Restrict at the network layer or add a service token before production.

**Schema:** `Notifications` (TypeId, UserId, Subject, Message, IsRead/IsSent, SentAt/ReadAt, Priority, Channel, ExternalId, ErrorMessage, RetryCount), `NotificationTypes` (8 seeded), `NotificationTemplates` (incl. a **`Language` column** — i18n-ready), `UserNotificationPreferences` (per-channel opt-in: email/SMS/push/marketing/booking/security/newsletter). Migration: `InitialNotificationSchema`.

---

## 6. ApiGateway (Ocelot, no database)

Single source of truth: `ocelot.json`. Path-preserving routes; JWT validated **at the gateway** for protected routes using the unified `Bearer` scheme:

| Upstream | Downstream port | Auth at gateway |
|---|---|---|
| `/api/auth/users/{everything}` | 7019 | **Admin** (`RouteClaimsRequirement` on `role`) |
| `/api/auth/{everything}` | 7019 | open (service enforces) |
| `/api/listings/{everything}` GET | 7002 | open |
| `/api/listings/{everything}` POST/PUT/DELETE/PATCH | 7002 | Bearer |
| `/api/search/{everything}` | 7002 | open |
| `/api/bookings/{everything}` GET | 7292 | open |
| `/api/bookings/{everything}` POST/PUT/DELETE | 7292 | Bearer |
| `/api/conversations/{everything}`, `/api/messages/{everything}` | 7179 | Bearer |
| `/api/fileupload/{everything}` | 7179 | open (service enforces upload) |
| `/api/sms/{everything}` | 7284 | open (service enforces) |

Plus `/health`. The internal `/api/notifications/*` endpoints are intentionally not routed. Functional, no stubs. JWT key/issuer/audience are unified across the gateway and all services.

---

## Phase 1 scope vs. actual state

| # | Phase 1 item | Status | Notes |
|---|---|---|---|
| 1 | Owner onboarding + listing (short-term/long-term/sale) | **Done** | `become-host` onboarding + listing CRUD; `ListingCategory` (ShortTerm/LongTerm/Sale) live as the `Category` column. |
| 2 | Photo uploads | **Done** | Real `IFormFile` → Azure Blob → URL stored as `ListingImage.ImageUrl`. |
| 3 | Search with filters | **Done** | Location, price, capacity, type, category, amenities, pagination, sort. (Rating sort is a stub.) |
| 4 | Map view support | **Done** | Lat/Long stored + returned; bounding-box filter in search. |
| 5 | Short-term booking + availability calendar | **Done\*** | Create (+capacity validation) and date-range calendar work. *Caveat: no confirm/reject endpoint — a booking can only be created or cancelled via the API.* |
| 6 | JazzCash payment | **Not Started** | No gateway code at all; only a `BookingPayments` DB scaffold. |
| 7 | Inquiry/messaging (long-term & sale) | **Done\*** | Full conversations/messages/real-time/attachments, linked to listings/bookings. *Caveats: new-message notifications not wired; user-name enrichment was mock.* |
| 8 | Email + SMS notifications | **Partial** | Sending capability works (SendGrid/SMTP + Twilio) and is callable, but **nothing triggers it on app events** (no event/bus wiring). Push is stubbed (no device-token store). |
| 9 | Featured/boosted listings | **Done** | `IsFeatured`/`FeaturedUntil` columns, real featured query, Host/Admin toggle. (No paid-boost flow yet — as scoped.) |
| 10 | Admin capabilities | **Partial** | Role system (Guest/Host/Admin) + Admin-gated `set-role` endpoint + gateway enforcement exist, but there are **no admin operations endpoints** (no user/listing moderation, no verification-approval endpoint, no "all bookings" view). |

**Only #6 (JazzCash) remains Not Started.** #8 and #10 are Partial; everything else is Done (5 and 7 with minor caveats).

---

## Existing functionality beyond the Phase 1 list

These are built and present in code/schema but were outside the original Phase 1 discussion:

1. **ID verification (KYC)** — `Verifications` table + full `VerificationService` (submit/update/approve/reject by status) + `verification-update` endpoint. (Admin approval exists as a service method but has no admin endpoint wired to it.)
2. **OTP verification** — phone (Twilio) and email (SendGrid) one-time-code flows.
3. **SignalR real-time chat** — live delivery, typing indicators, read receipts, and file/image attachments in messages.
4. **Notification preferences + templating** — granular per-channel `UserNotificationPreferences` and `NotificationTemplates` with a `Language` column (i18n groundwork).
5. **Multi-guest bookings** — `BookingGuests` captures multiple named guests per booking.
6. **Discovery endpoints** — `/api/search/similar/{id}`, `/popular-locations`, `/stats`.
7. **SMS utilities** — `validate-phone` / `format-phone`.
8. **Reviews groundwork** — `BookingReview` entity preserved at `WeStay.BookingService/Future/` for a later phase.

---

## Quality / testing

- **Integration tests:** `WeStay.Tests` (xUnit, HTTP-level through the gateway) — **22/22 passing**. Covers the auth chain, role system, listing CRUD + category, price/capacity cross-service calls, booking flow + capacity validation, and featured listings.
- **Migrations:** all five data services use EF Core migrations on their own databases; `EnsureCreated()` removed everywhere.
- **Secrets:** no secrets committed; all read from User Secrets / environment variables. `appsettings.json` files are placeholder-only templates; `appsettings.Development.json`/`Production.json` are git-ignored.

---

## Recommended next priorities

1. **JazzCash payment integration (#6)** — the only Not-Started Phase 1 item; unblocks the end-to-end paid booking flow. Build it as a payment module/service that fills the existing `BookingPayments` scaffold (initiate + webhook).
2. **Wire notifications to events (#8)** — connect booking/auth events to NotificationService (direct HTTP now, message bus later) so Email/SMS actually fire on real events.
3. **Booking lifecycle** — expose confirm/reject endpoints so hosts can approve bookings (the service logic already exists).
4. **Admin operations (#10)** — add admin endpoints (user/listing moderation, verification approval, bookings overview) on top of the existing role system.
5. **Schema cleanup** — remove the redundant `Bookings.BookingStatusId` shadow FK via an explicit relationship config + migration.
6. **Push notifications** — implement the FCM device-token store so push can actually deliver.
