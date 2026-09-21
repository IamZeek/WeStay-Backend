# WeStay — Deployment Cost Brief

Two parts:

1. **Cost inventory** — every paid dependency found by auditing the codebase, with what it's wired to and where it's configured.
2. **Estimation prompt** — a self-contained brief to paste into other AI tools so you get comparable estimates.

Audited 2026-09-12 against the `WeStay.sln` working tree.

---

## Part 1 — Cost inventory

### A. Infrastructure you must pay for

| Item | Detail |
| --- | --- |
| Compute — 7 .NET 8 processes | ApiGateway (7087), AuthService (7019), ListingService (7002), BookingService (7292), MessagingService (7179), NotificationService (7284), ReviewService (7084) |
| Databases — 6 separate SQL Server DBs | `WeStayAuth`, `WeStayListing`, `WeStayBooking`, `WeStayMessaging`, `WeStayNotification`, `WeStayReview` |
| Frontend hosting | React 18 + Vite static bundle (`westay-portal`) |
| Domain + TLS | TLS is free via Let's Encrypt / Caddy |
| Backups | 6 databases + the blob container |

**SQL Server licensing is the swing factor.** Express is free for production but capped at
**10 GB per database, 4 cores, and a 1.4 GB buffer pool**. Six databases on one Express
instance is legal and each gets its own 10 GB. Managed SQL (Azure SQL et al.) removes the
caps at roughly $5–15 per database per month.

### B. Third-party paid services — confirmed in code

| Service | NuGet package | Used by | Config key | Cost driver |
| --- | --- | --- | --- | --- |
| **Twilio** (SMS) | `Twilio` | AuthService, NotificationService | `Twilio:AccountSid/AuthToken/FromPhone`, `TwilioSettings:*` | **$0.4734/SMS to Pakistan** (~PKR 133) |
| **SendGrid** (email) | `SendGrid` | AuthService, NotificationService | `SendGrid:ApiKey/FromEmail/FromName` | Free tier is now a **60-day trial**, then from $19.95/mo |
| **Cloudflare R2** (listing photos) | `AWSSDK.S3` | ListingService | `R2:AccountId`, `AccessKeyId`, `SecretAccessKey`, `PublicBaseUrl`, `BucketName` | $0.015/GB/month, **zero egress**; 10 GB/month free |
| **Safepay** (payment gateway, Pakistan) | `SFPY.net` | BookingService | `Safepay:Environment/ApiKey/V1Secret/WebhookSecret` — currently `sandbox` | Per-transaction % + fixed fee |
| **Firebase** (push) | `FirebaseAdmin`, `Google.Cloud.Firestore` | NotificationService | `PushNotificationSettings:PublicKey/PrivateKey/Subject` | FCM push is free; **Firestore is billable if actually used** |
| **Google OAuth** | `Google.Apis.Auth`, `Microsoft.AspNetCore.Authentication.Google` | AuthService | `Authentication:Google:ClientId/ClientSecret` | Free |
| **WhatsApp Business API** | — not implemented — | — | — | Planned; needs a BSP + Meta business verification |

### C. Free / open-source — no licence cost

Ocelot (gateway), BCrypt.Net-Next, EF Core + SqlServer provider, Swashbuckle, xUnit,
and **self-hosted SignalR** in MessagingService (in-framework, not Azure SignalR Service).

### D. Config placeholders that are NOT wired — no cost yet

Easy to mistake these for real dependencies when estimating:

- **Redis** — `ConnectionStrings:Redis = localhost:6379` in MessagingService, but there is
  **no Redis NuGet package** in the project. Not used.
- **Azure Service Bus** — `ConnectionStrings:ServiceBus`, empty. Not used. Confirmed by
  `CLAUDE.md`: no message bus; cross-service calls are direct HTTP.
- **PaymentService** — `Services:PaymentService = https://localhost:7004` in BookingService,
  but no such project exists in the solution. An 8th service that was never built.

### E. Deployment blockers found during the audit

1. **Safepay redirect URLs point at a dead ngrok tunnel** — `Safepay:SuccessUrl` and
   `CancelUrl` are set to `https://759c-182-191-89-142.ngrok-free.app/payments/...`.
   A dev artifact; must become real HTTPS URLs before payments work.
2. **MessagingService cannot scale horizontally.** SignalR without a Redis backplane
   requires a single instance or sticky sessions.
3. **NotificationService email is pointed at `smtp.gmail.com`** (`EmailSettings`). Gmail
   SMTP caps around 500/day and isn't intended for transactional mail.
4. **Six databases × 10 GB** if you stay on SQL Express. Messaging will hit it first.
5. **OTP storage is broken.** `PhoneVerificationService` keeps OTPs in an instance
   `Dictionary` but is registered `AddScoped`, so send and verify land on different
   instances and verification always fails. Email OTP uses `IMemoryCache` — works
   single-instance, dies on restart, breaks with more than one replica.

---

## Part 2 — Estimation prompt

Copy everything between the lines into ChatGPT / Gemini / Grok / DeepSeek to get a
comparable estimate.

---

I need a detailed monthly hosting cost estimate for a pre-launch startup. Be specific
with numbers, name actual providers and SKUs, and state your assumptions. Where you're
uncertain, say so rather than guessing.

**Product**

An Airbnb-style short-stay rental marketplace called WeStayy, launching in Islamabad and
Rawalpindi, Pakistan. Currently pre-launch: a host portal where ~100 "founding hosts"
sign up and manage property listings. Guest-facing booking comes later.

**Team and funding**

Solo/small team, low funding, based in Pakistan. Cost sensitivity is high. The audience is
entirely Pakistani.

**Backend architecture — .NET 8 microservices**

Seven separate ASP.NET Core processes behind an Ocelot API gateway:

| Service | Owns |
| --- | --- |
| ApiGateway | Ocelot routing, JWT validation, CORS |
| AuthService | Users, JWT issuance, roles, Google OAuth, OTP, KYC records |
| ListingService | Listings, search, amenities, images, featured, cached ratings |
| BookingService | Bookings, availability, booking lifecycle, payments |
| MessagingService | Conversations, messages, SignalR real-time, attachments |
| NotificationService | Email, SMS, templates, preferences |
| ReviewService | Reviews and star ratings |

- **Six separate SQL Server databases**, one per data service, EF Core with migrations.
- Cross-service communication is **direct HTTP** (no message bus, no Redis, no queue).
- SignalR is **self-hosted in-process** with no Redis backplane, so MessagingService is
  currently single-instance only.
- Two background jobs in BookingService poll hourly (auto-complete and auto-cancel).

**Frontend**

React 18 + Vite, static SPA build, client-side routed. Needs SPA fallback rewrites.

**Third-party services wired in code**

- Twilio (SMS) — for OTP and booking notifications
- SendGrid (transactional email)
- Azure Blob Storage (listing photos, up to 10 MB each)
- Safepay (Pakistani payment gateway) — currently sandbox
- Firebase Admin / FCM (push, not yet fully wired)
- Google OAuth (free)

**Expected volumes for the first 3 months**

- ~100 hosts, ~300 property listings
- ~5–10 photos per listing, ~500 KB each → roughly 1–2 GB of images
- Under 1,000 API requests/day
- A few hundred emails and SMS per month
- All six databases well under 10 GB each

**Constraints specific to Pakistan — please account for these**

1. **Payment friction.** Pakistani debit and credit cards are frequently declined by AWS,
   GCP, Azure, and Hetzner (SBP foreign-exchange policy, 3D Secure and recurring-auth
   requirements, provider risk scoring). Since January 2026 SBP's approved-provider list
   names Amazon, Google, and Microsoft, letting a registered Pakistani business remit up
   to $200,000/year through banking channels — but that needs an NTN and business bank
   account, and does **not** cover smaller providers like Hetzner.
2. **Latency.** Users are all in Pakistan. There is no local region for AWS, Azure, or GCP.
   Approximate round-trip times: local Pakistani datacenter 5–20 ms, Azure UAE North or
   AWS Bahrain 30–50 ms, Singapore 80–100 ms, Germany 130–150 ms. Note that Mumbai is not
   reliably fast despite being close, because Pakistan–India peering is limited and traffic
   often routes via Europe or Singapore.
3. **PKR billing.** Local Pakistani providers (PTCL Smart Cloud, QCloud, HostBreak,
   CloudVPS.pk) bill in PKR via bank transfer, JazzCash, or Easypaisa — no forex spread and
   no 0.5% withholding tax on international card transactions.

**What I want from you**

1. A recommended deployment topology, with the specific provider and instance SKU.
2. A line-item monthly cost table: compute, database, storage, bandwidth, TLS, backups,
   domain. Give totals in **USD and PKR** (state the exchange rate you used).
3. Whether to use **SQL Server Express** (free, but 10 GB per database, 4 cores, 1.4 GB
   buffer pool) versus paid managed SQL, and what that decision costs.
4. At least **three costed options**: cheapest viable, balanced, and fully managed.
5. The estimated per-message cost of SMS versus WhatsApp versus email for OTP delivery to
   Pakistani numbers, and which you'd pick.
6. What breaks first as usage grows, and the approximate user count or data volume at which
   each limit bites.
7. Any cost I've likely overlooked.

Do not recommend a serverless rewrite or an architecture change — price what exists. I'm
aware that seven services and six databases is heavy for this stage; assume it stays as is.

---

## Part 3 — My own estimate, for comparison

State the exchange rate: **PKR 280 = USD 1**.

### Option 1 — Cheapest viable (recommended for now)

| Line item | Detail | USD/mo | PKR/mo |
| --- | --- | --- | --- |
| Compute | Local Pakistani VPS, 4 vCPU / 8 GB / 80 GB NVMe, all 7 services via Docker Compose | ~$15–25 | ~4,000–7,000 |
| Database | SQL Server Express on the same box, all 6 DBs | $0 | 0 |
| Frontend | Cloudflare Pages | $0 | 0 |
| Images | Cloudflare R2, 10 GB free tier | $0 | 0 |
| Email | Brevo, 300/day free | $0 | 0 |
| TLS | Caddy + Let's Encrypt | $0 | 0 |
| Backups | Nightly dump to R2 | $0 | 0 |
| Domain | ~$12/year | ~$1 | ~280 |
| **Total** | | **~$16–26** | **~4,500–7,300** |

Billed in PKR, so no declined cards and no forex loss. Lowest latency for Pakistani users.

### Option 2 — Balanced

Hetzner CX33 in Germany at €8.49/month, same software layout. **~$12–14/month.** Cheaper
and far more reliable than local providers, but 130–150 ms latency and you're paying by
card, which Hetzner's non-SBP-approved status makes fragile from Pakistan.

### Option 3 — Fully managed

Azure UAE North — App Service B2 or S1 plus 6 × Azure SQL Basic.
**~$60–90/month (PKR 17,000–25,000).** Roughly 4–5× Option 1. Buys managed backups,
patching, easy scaling, ~40 ms latency, and SBP-approved remittance.

### Verification channel, per message

| Channel | Per message | Notes |
| --- | --- | --- |
| Email (Brevo) | **PKR 0** | Free to 300/day; already implemented in `AuthController` |
| WhatsApp via local BSP | ~PKR 4.30 | Needs Meta business verification, 1–2 weeks minimum |
| Local PTA short-code SMS | ~PKR 4.75 | Rs 10,000 minimum package, needs NTN |
| **Twilio SMS (current code)** | **~PKR 133** | 28× the local rate |

### What breaks first

1. **SQL Express 1.4 GB buffer pool** — binds before CPU or disk. Migrate to PostgreSQL
   (free, no caps) or pay for SQL Standard.
2. **10 GB per database** — Messaging first.
3. **MessagingService single-instance** — needs a Redis backplane to scale out.
4. **Single point of failure** — no HA. Acceptable pre-launch, not once hosts earn income
   through the platform.

### Costs commonly overlooked

- Twilio SMS on booking events: `BookingService.cs` lines 557 and 574 send SMS **and**
  email, so every completed booking currently costs ~PKR 266 in Twilio charges alone.
- Safepay per-transaction fees once payments go live.
- ~~Azure Blob egress~~ — no longer a cost: R2 charges **zero egress**, which was the main
  reason to move off Blob. Storage is $0.015/GB/month with the first 10 GB free, so the
  photo bill for a founding cohort is realistically zero.
- Firestore, if `Google.Cloud.Firestore` ends up actually used.
- SendGrid's free tier is gone: 60-day trial, then $19.95/month minimum.
