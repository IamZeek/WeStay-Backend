# SQL Server → PostgreSQL migration

Clean-slate migration: no data preserved, all migrations regenerated.
Code changes are done; the EF CLI steps below are yours to run.

## What changed in code

| File(s) | Change |
| --- | --- |
| 6 × `*.csproj` | `Microsoft.EntityFrameworkCore.SqlServer` 8.0.19 → `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.10 |
| 6 × `Program.cs` | `options.UseSqlServer(...)` → `options.UseNpgsql(...)` |
| 6 × `Program.cs` | Added `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` — see **Gotchas** |
| `README.md` | Connection-string examples converted to Npgsql format; added the missing ReviewService block |

Services touched: Auth, Listing, Booking, Messaging, Notification, Review.
ApiGateway has no database and was not modified.

### What did NOT need changing

- **No raw SQL anywhere.** No `FromSqlRaw`, `ExecuteSqlRaw`, `HasDefaultValueSql`, or computed
  columns — the usual blockers in a provider swap.
- **No `DateTime.Now`.** All timestamps already use `DateTime.UtcNow`.
- **`[Column(TypeName = "decimal(18,2)")]`** and friends in `Listing.cs`, `Payment.cs`,
  `PlatformFeeConfig.cs` — `decimal(p,s)` is standard SQL and valid in PostgreSQL
  (an alias for `numeric(p,s)`). Left as-is.
- **`HasData` seeding** in five DbContexts regenerates automatically.
- **`appsettings.json`** connection strings were already empty placeholders; real values live in
  User Secrets.

---

## Steps

### 1. Run PostgreSQL

```bash
docker run -d --name westay-postgres \
  -e POSTGRES_USER=westay \
  -e POSTGRES_PASSWORD=<choose-a-password> \
  -e POSTGRES_DB=westay_auth \
  -p 5432:5432 \
  -v westay_pgdata:/var/lib/postgresql/data \
  postgres:16
```

### 2. Create the remaining five databases

```bash
docker exec -it westay-postgres psql -U westay -d westay_auth -c "
  CREATE DATABASE westay_listing;
  CREATE DATABASE westay_booking;
  CREATE DATABASE westay_messaging;
  CREATE DATABASE westay_notification;
  CREATE DATABASE westay_review;"
```

### 3. Delete the old migrations

Every existing migration has SQL Server types (`nvarchar`, `datetime2`, `bit`) baked in, so
they can't be reused. From the repo root:

```powershell
# PowerShell
Remove-Item -Recurse -Force `
  WeStay.AuthService\Migrations, `
  WeStay.ListingService\Migrations, `
  WeStay.BookingService\Migrations, `
  WeStay.MessagingService\Migrations, `
  WeStay.NotificationService\Migrations, `
  WeStay.ReviewService\Migrations
```

### 4. Restore packages

```bash
dotnet restore WeStay.sln
```

If `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.10` isn't found, bump to the newest 8.0.x —
stay on **8.x** to match EF Core 8.

### 5. Set connection strings

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection"      "Host=localhost;Port=5432;Database=westay_auth;Username=westay;Password=<db-password>"         --project WeStay.AuthService
dotnet user-secrets set "ConnectionStrings:ListingDbConnection"    "Host=localhost;Port=5432;Database=westay_listing;Username=westay;Password=<db-password>"      --project WeStay.ListingService
dotnet user-secrets set "ConnectionStrings:BookingConnection"      "Host=localhost;Port=5432;Database=westay_booking;Username=westay;Password=<db-password>"      --project WeStay.BookingService
dotnet user-secrets set "ConnectionStrings:MessagingConnection"    "Host=localhost;Port=5432;Database=westay_messaging;Username=westay;Password=<db-password>"    --project WeStay.MessagingService
dotnet user-secrets set "ConnectionStrings:NotificationConnection" "Host=localhost;Port=5432;Database=westay_notification;Username=westay;Password=<db-password>" --project WeStay.NotificationService
dotnet user-secrets set "ConnectionStrings:ReviewConnection"       "Host=localhost;Port=5432;Database=westay_review;Username=westay;Password=<db-password>"       --project WeStay.ReviewService
```

### 6. Regenerate migrations and create the schemas

```bash
dotnet ef migrations add InitialPostgres --project WeStay.AuthService
dotnet ef database update                --project WeStay.AuthService

dotnet ef migrations add InitialPostgres --project WeStay.ListingService
dotnet ef database update                --project WeStay.ListingService

dotnet ef migrations add InitialPostgres --project WeStay.BookingService
dotnet ef database update                --project WeStay.BookingService

dotnet ef migrations add InitialPostgres --project WeStay.MessagingService
dotnet ef database update                --project WeStay.MessagingService

dotnet ef migrations add InitialPostgres --project WeStay.NotificationService
dotnet ef database update                --project WeStay.NotificationService

dotnet ef migrations add InitialPostgres --project WeStay.ReviewService
dotnet ef database update                --project WeStay.ReviewService
```

### 7. Verify

```bash
dotnet build WeStay.sln
```

Start Auth, Listing and the Gateway, then re-run the smoke test from the browser console
(register → become-host → create listing → edit → deactivate → reactivate). Expect the same
200/201 results as on SQL Server.

Confirm seed data landed:

```bash
docker exec -it westay-postgres psql -U westay -d westay_listing -c 'SELECT COUNT(*) FROM "Amenities";'
docker exec -it westay-postgres psql -U westay -d westay_booking  -c 'SELECT * FROM "BookingStatuses";'
```

The double quotes matter — see Gotchas.

---

## Gotchas

### DateTime Kind (the one that will actually bite)

Npgsql 6+ maps `DateTime` to `timestamp with time zone` and **throws** if the value's `Kind`
is `Unspecified` or `Local`. Values deserialized from JSON — `RegisterRequest.DateOfBirth`,
booking `CheckInDate` / `CheckOutDate` — always arrive as `Unspecified`.

Without mitigation, registration and booking creation fail at runtime with:

```
Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'
```

**Already handled:** `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` is set
at the top of all six `Program.cs` files, restoring SQL Server-equivalent
`timestamp without time zone` mapping.

This is a pragmatic shim, not a permanent fix. The cleaner long-term options are to change
`DateOfBirth` to `DateOnly`, and normalise incoming `DateTime` values to UTC at the model-binding
layer. Do that when you're not mid-migration.

### Identifier case sensitivity

PostgreSQL folds unquoted identifiers to lowercase. EF Core quotes everything, so your tables
stay `"Listings"`, `"Users"`, and so on — the application is unaffected. But **hand-written psql
queries need double quotes**:

```sql
SELECT * FROM "Listings";   -- works
SELECT * FROM Listings;     -- ERROR: relation "listings" does not exist
```

### Identity columns

SQL Server `IDENTITY` becomes PostgreSQL `GENERATED BY DEFAULT AS IDENTITY`. EF handles this
automatically. Because you're starting empty there are no sequences to reset.

### Enums

Your enums (`UserRole`, `ListingType`, `ListingStatus`, `ListingCategory`) are stored as
integers and stay integers. No change, and the front end's integer mapping in
`listingOptions.js` remains correct.

### Connection string format

Npgsql uses a different syntax — `Host=`/`Username=`, not `Server=`/`Trusted_Connection=`.
`MultipleActiveResultSets` and `TrustServerCertificate` have no Npgsql equivalent and will
throw if left in.

### Integration tests

`WeStay.Tests` runs HTTP-level through the gateway and holds no database connection, so it
needs no changes — but it does need all six services running against the new databases.

---

## What this unlocks

The SQL Express limits from the deployment brief are gone:

| Limit | SQL Server Express | PostgreSQL |
| --- | --- | --- |
| Database size | 10 GB each | Unlimited |
| RAM (buffer pool) | 1.4 GB | Unlimited |
| CPU cores | 4 | Unlimited |
| Licence | Free, with the caps above | Free, no caps |

This removes items 1 and 2 from the "what breaks first" list in `DEPLOYMENT-COST-BRIEF.md`.
Hosting cost is unchanged — Postgres runs on the same box — but the ceiling is now your VPS
rather than a licence restriction.
