# Church Rooms

Room booking and occupancy app for a church building. Every room door has a QR code. Scanning it opens a public page showing who has the room right now and what's booked next, so nobody has to argue with whoever's sitting in an empty room.

- **Anyone** (no login): see each room's live status and the full schedule.
- **Leaders**: register, wait for Admin approval, then request rooms (one-time or weekly), skip a week, set an end date, cancel, or ask for a change.
- **Admins**: approve accounts and requests, edit any booking, manage rooms, print QR codes.

The schedule can never contain two approved bookings in the same room at the same time. That holds for Admins too ([ADR-0001](docs/adr/0001-no-overlapping-approved-bookings.md)).

## Stack

.NET 10 · ASP.NET Core Blazor (static server rendering) · EF Core + PostgreSQL · ASP.NET Core Identity · Tailwind CSS (standalone CLI) · xUnit

Domain glossary: [CONTEXT.md](CONTEXT.md). Decisions: [docs/adr](docs/adr). Code tour for Laravel developers: [docs/walkthrough.md](docs/walkthrough.md).

## Run it locally

Needs the .NET 10 SDK and PostgreSQL running locally.

```bash
dotnet tool restore     # installs dotnet-ef (pinned in dotnet-tools.json)

cd src/RoomBooking
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=room_booking;Username=$(whoami)"
dotnet user-secrets set "SeedAdmin:Email" "admin@church.local"
dotnet user-secrets set "SeedAdmin:Password" "<pick one: 8+ chars, upper, digit, symbol>"
dotnet user-secrets set "SeedAdmin:Name" "Church Admin"

dotnet run              # http://localhost:5273
```

On startup the app applies migrations (creating the database if needed), creates the first Admin from the secrets above, and in Development adds 3 sample rooms.

The first build downloads the Tailwind CLI into `src/RoomBooking/.tools/` and compiles `Styles/app.css` into `wwwroot/app.css`. When editing markup with `dotnet watch`, run the Tailwind watcher in a second terminal:

```bash
cd src/RoomBooking
.tools/tailwindcss-macos-arm64 -i Styles/app.css -o wwwroot/app.css --watch
```

### Tests

```bash
dotnet test
```

Covers the two rules that can silently corrupt the schedule: weekly occurrence expansion and the clash check.

### QR codes

Admin → Rooms & QR codes → Print QR codes. Set `Church:PublicBaseUrl` (e.g. `https://rooms.yourchurch.org`) first, or the codes will point at `localhost`.
