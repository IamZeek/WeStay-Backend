# WeStay Backend

.NET 8 microservices solution for the WeStay property-rental platform. See [PROJECT_STATUS.md](PROJECT_STATUS.md) for the full architecture/feature audit.

Projects: `WeStay.ApiGateway`, `WeStay.AuthService`, `WeStay.ListingService`, `WeStay.BookingService`, `WeStay.MessagingService`, `WeStay.NotificationService`, `WeStay.ReviewService`, `WeStay.Common`.

---

## Configuration & Secrets

**No secrets are committed to this repository.** Every `appsettings.json` is a **placeholder-only template** — all secret values are empty strings. Each service reads its secrets at runtime from **.NET User Secrets** (local dev) or **environment variables / a secret store** (deployed).

Services **fail fast at startup** if a required secret is missing:
- A missing/short (`< 32` chars) JWT signing key throws `InvalidOperationException`.
- A missing database connection string throws `InvalidOperationException`.

`appsettings.Development.json` and `appsettings.Production.json` are **git-ignored** and must never be committed. Put machine-specific or secret overrides there, or (preferred for secrets) in User Secrets.

### What each service needs

| Service | Required secret keys | Optional / feature keys |
|---------|----------------------|-------------------------|
| `WeStay.AuthService` | `ConnectionStrings:DefaultConnection`, `Jwt:Key` | `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`, `Authentication:Facebook:AppId`, `Authentication:Facebook:AppSecret`, `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromPhone`, `SendGrid:ApiKey` |
| `WeStay.ListingService` | `ConnectionStrings:ListingDbConnection`, `Jwt:Key` | — |
| `WeStay.BookingService` | `ConnectionStrings:BookingConnection`, `Jwt:Key` | — |
| `WeStay.MessagingService` | `ConnectionStrings:MessagingConnection`, `Jwt:Key` ⚠️ | `EmailSettings:UserName`, `EmailSettings:Password`, `SmsSettings:TwilioAccountSid`, `SmsSettings:TwilioAuthToken`, `AzureServiceBus:ConnectionString` |
| `WeStay.NotificationService` | `ConnectionStrings:NotificationConnection`, `Jwt:Key` | `EmailSettings:UserName`, `EmailSettings:Password`, `TwilioSettings:AccountSid`, `TwilioSettings:AuthToken`, `TwilioSettings:FromNumber`, `PushNotificationSettings:PublicKey`, `PushNotificationSettings:PrivateKey` |
| `WeStay.ApiGateway` | `JwtSettings:Secret` | — |

> ⚠️ **MessagingService key-name caveat:** `Program.cs` reads `Jwt:Key` and `ConnectionStrings:MessagingConnection`, but the committed `appsettings.json` template still uses the legacy section names `JwtSettings:Secret` and `ConnectionStrings:WeStayDatabase`. This pre-existing mismatch is tracked in PROJECT_STATUS.md (§3.1, §4.2) and was intentionally **not** fixed in the secrets cleanup. Set your User Secrets under the keys the code actually reads: `Jwt:Key` and `ConnectionStrings:MessagingConnection`.

> **JWT key sharing:** AuthService signs tokens; the Gateway and the other services validate them. For end-to-end auth to work, set the **same** value for `Jwt:Key` across Auth/Listing/Booking/Messaging/Notification and for `JwtSettings:Secret` in the Gateway. (Issuer/Audience are still inconsistent across services — see PROJECT_STATUS.md punch-list item #1; that unification is separate work.)

### Generating a strong JWT key (≥ 32 chars)

```bash
# bash / git-bash
openssl rand -base64 48
```
```powershell
# PowerShell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))
```

### Setting User Secrets (local dev)

Each project already has a `<UserSecretsId>` configured, so you can set secrets directly. Run from the repo root (`--project` targets each service). Replace the example values with your own.

```bash
# --- AuthService ---
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=<your-server>;Database=WeStay;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true;" --project WeStay.AuthService
dotnet user-secrets set "Jwt:Key" "<shared-32+char-key>" --project WeStay.AuthService
dotnet user-secrets set "Authentication:Google:ClientId" "<google-client-id>" --project WeStay.AuthService
dotnet user-secrets set "Authentication:Google:ClientSecret" "<google-client-secret>" --project WeStay.AuthService
# Optional (only if these providers are used):
dotnet user-secrets set "Authentication:Facebook:AppId" "<fb-app-id>" --project WeStay.AuthService
dotnet user-secrets set "Authentication:Facebook:AppSecret" "<fb-app-secret>" --project WeStay.AuthService
dotnet user-secrets set "Twilio:AccountSid" "<twilio-sid>" --project WeStay.AuthService
dotnet user-secrets set "Twilio:AuthToken" "<twilio-token>" --project WeStay.AuthService
dotnet user-secrets set "Twilio:FromPhone" "<twilio-phone>" --project WeStay.AuthService
dotnet user-secrets set "SendGrid:ApiKey" "<sendgrid-key>" --project WeStay.AuthService

# --- ListingService ---
dotnet user-secrets set "ConnectionStrings:ListingDbConnection" "Server=<your-server>;Database=WeStay;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true;" --project WeStay.ListingService
dotnet user-secrets set "Jwt:Key" "<shared-32+char-key>" --project WeStay.ListingService

# --- BookingService ---
dotnet user-secrets set "ConnectionStrings:BookingConnection" "Server=<your-server>;Database=WeStay;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true;" --project WeStay.BookingService
dotnet user-secrets set "Jwt:Key" "<shared-32+char-key>" --project WeStay.BookingService

# --- MessagingService (note the key names the code reads) ---
dotnet user-secrets set "ConnectionStrings:MessagingConnection" "Server=<your-server>;Database=WeStayMessaging;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true;" --project WeStay.MessagingService
dotnet user-secrets set "Jwt:Key" "<shared-32+char-key>" --project WeStay.MessagingService
# Optional notification providers:
dotnet user-secrets set "EmailSettings:UserName" "<smtp-user>" --project WeStay.MessagingService
dotnet user-secrets set "EmailSettings:Password" "<smtp-app-password>" --project WeStay.MessagingService
dotnet user-secrets set "SmsSettings:TwilioAccountSid" "<twilio-sid>" --project WeStay.MessagingService
dotnet user-secrets set "SmsSettings:TwilioAuthToken" "<twilio-token>" --project WeStay.MessagingService

# --- NotificationService ---
dotnet user-secrets set "ConnectionStrings:NotificationConnection" "Server=<your-server>;Database=WeStay;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true;" --project WeStay.NotificationService
dotnet user-secrets set "Jwt:Key" "<shared-32+char-key>" --project WeStay.NotificationService
# Optional providers:
dotnet user-secrets set "EmailSettings:UserName" "<smtp-user>" --project WeStay.NotificationService
dotnet user-secrets set "EmailSettings:Password" "<smtp-app-password>" --project WeStay.NotificationService
dotnet user-secrets set "TwilioSettings:AccountSid" "<twilio-sid>" --project WeStay.NotificationService
dotnet user-secrets set "TwilioSettings:AuthToken" "<twilio-token>" --project WeStay.NotificationService
dotnet user-secrets set "TwilioSettings:FromNumber" "<twilio-phone>" --project WeStay.NotificationService

# --- ApiGateway ---
dotnet user-secrets set "JwtSettings:Secret" "<shared-32+char-key>" --project WeStay.ApiGateway
```

### Deployed environments (env vars)

Use the standard .NET double-underscore convention. Examples:

```
ConnectionStrings__DefaultConnection=...
Jwt__Key=...
Authentication__Google__ClientSecret=...
JwtSettings__Secret=...        # ApiGateway
```

Or bind a secret store (e.g. Azure Key Vault) in each `Program.cs` via `builder.Configuration.AddAzureKeyVault(...)`. (Not yet wired up — see PROJECT_STATUS.md.)
