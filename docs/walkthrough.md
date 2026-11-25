# Code walkthrough (for Laravel developers)

A tour of the codebase in the order a request flows through it, with the Laravel equivalent for each piece.

## Project layout

```
RoomBooking.slnx                 solution file: groups projects (no Laravel equivalent; think monorepo index)
global.json                      pins the .NET SDK version (like .nvmrc / composer's platform.php)
dotnet-tools.json                local CLI tools, here dotnet-ef (like composer require --dev for a CLI)
src/RoomBooking/
  Program.cs                     bootstrap/app.php + config/app.php providers + route registration, in one file
  appsettings.json               config/*.php defaults. Secrets go in user-secrets (like .env, outside the repo)
  Data/                          Eloquent models, DbContext, migrations, seeder
  Scheduling/                    pure domain rules (no framework, no DB)
  Bookings/                      BookingService (all booking writes) + BookingForm (form model)
  Endpoints/                     POST action routes (one-click buttons)
  Components/                    Razor components = Blade views + Livewire-style components
  Styles/app.css                 Tailwind input
tests/RoomBooking.Tests/         xUnit (PHPUnit)
```

## 1. Startup: `Program.cs`

Two halves:

- **`builder.Services.Add...`** registers services in the DI container, which is Laravel's service container. Lifetimes:
  - `AddSingleton<ChurchClock>()`: one instance for the whole app (`$this->app->singleton`).
  - `AddScoped<BookingService>()`: one instance per HTTP request (`$this->app->scoped`). `ApplicationDbContext` is scoped too, so everything in one request shares one DB connection and change tracker.
  - Constructor injection works like Laravel's: `public class BookingService(ApplicationDbContext db, ChurchClock clock)` is a C# 12 *primary constructor*; the container fills the parameters.
- **`app.Use...` / `app.Map...`** builds the middleware pipeline and routes, in order. `UseAntiforgery()` is `VerifyCsrfToken`. `MapRazorComponents<App>()` routes every `@page` component, which is your `routes/web.php`.

## 2. Data: EF Core ≈ Eloquent

- **Models** are plain classes: `Data/Room.cs`, `Data/Booking.cs`. Properties are columns. `[MaxLength(100)]` sets column size and doubles as validation.
- **`ApplicationDbContext`** is the DB connection plus the list of tables (`DbSet<Room> Rooms`), and `OnModelCreating` holds what `$casts` / indexes / foreign keys would. `HasConversion<string>()` stores enums as text, like an enum cast.
- **Queries** are LINQ, which EF translates to SQL:
  ```csharp
  db.Bookings.Include(b => b.Room).Where(b => b.Status == BookingStatus.Approved).ToListAsync()
  // ≈ Booking::with('room')->where('status', 'Approved')->get()
  ```
  `Include` is eager loading (`with`). `AsNoTracking()` means read-only: EF won't watch the objects for changes.
- **Saving**: change properties on a tracked object, then `await db.SaveChangesAsync()`. No `->save()` per model; one call flushes every change in the request (unit of work).
- **Migrations** are generated from the model, not written by hand:
  ```bash
  dotnet ef migrations add AddSomething --project src/RoomBooking --output-dir Data/Migrations
  ```
  Compare `make:migration`, except EF diffs your C# model against the last snapshot and writes the migration for you. `Seed.RunAsync` applies them at startup (like running `migrate --force` + `db:seed` on boot).

## 3. Auth: ASP.NET Core Identity ≈ Breeze/Fortify

- `ApplicationUser : IdentityUser` is your `User` model, with `DisplayName` and `Status` added.
- Login/Register pages came from the project template (`Components/Account/Pages`). Breeze-style scaffolding: you own and edit the code.
- **Account approval** is `Data/ApprovedAccountConfirmation.cs`. Identity has a hook, `IUserConfirmation`, that's asked "is this user confirmed?" at sign-in. Normally it means "email verified"; here it means "Admin approved". One small class, and pending users can't sign in.
- **Roles**: `AddRoles<IdentityRole>()` plus `[Authorize(Policy = "Admin")]`. The policy is defined in `Program.cs` (`AddAuthorizationBuilder().AddPolicy(...)`), which is roughly a Gate. `Components/Pages/Admin/_Imports.razor` applies it to every page in that folder, like a route group with `->middleware('can:admin')`.
- `SecurityStampValidatorOptions.ValidationInterval = 1 minute` makes the auth cookie re-check the DB every minute, so "Make Admin" or a password reset takes effect without the user logging out.

## 4. Pages: Razor components ≈ Blade

A `.razor` file is markup plus a `@code { }` block, like a Blade view with its Livewire class in the same file.

- `@page "/rooms/{Id:int}"` is the route. `[Parameter] public int Id` receives it.
- `@inject BookingService Bookings` injects a service (like `app(BookingService::class)`).
- `OnInitializedAsync()` runs on each request before rendering, which is where your controller's data loading goes.
- `<RoomStatus Occurrences="..." />` is a child component, like `<x-room-status :occurrences="..." />`.

**Static SSR** (ADR-0002): the server renders HTML and sends it, nothing stays connected, and forms do normal POSTs. That's the classic Blade + controller model, *not* Livewire. Blazor *can* do Livewire-style live components ("interactive server"), but that keeps a WebSocket open, which is fragile on phones.

### Forms

Two styles, used for different jobs:

1. **`<EditForm>` in the page** for real forms with validation (`Book.razor`, `Admin/EditBooking.razor`).
   - `[SupplyParameterFromForm] BookingForm Input` is model binding: the POSTed fields fill this object (`$request->validated()` into a DTO).
   - Validation attributes (`[Required]`, `[MaxLength]`) plus `IValidatableObject.Validate()` for cross-field rules (`BookingForm.cs`) play the role of a FormRequest.
   - `OnValidSubmit="Submit"` runs only if validation passes.
   - Gotcha (found in testing): Blazor renders once at the first `await` in `OnInitializedAsync`, so `Input` must be set *before* awaiting anything, or `EditForm` throws. See the comment in `Book.razor`.
2. **Plain `<form method="post" action="actions/...">`** for one-click buttons (Approve, Withdraw, Skip). These hit minimal API endpoints in `Endpoints/ActionEndpoints.cs`: route + closure, like `Route::post('/x', fn () => ...)`. Each does the work and redirects back with a flash message (Post/Redirect/Get). `<AntiforgeryToken />` is `@csrf`.

Flash messages: `IdentityRedirectManager.RedirectToWithStatus` and `ActionEndpoints.Back` set a 5-second cookie; `StatusMessage` in the layout reads and deletes it. Same idea as `session()->flash()`.

## 5. The domain: where the rules live

- **`Scheduling/Schedule.cs`**: pure functions, with no DB or clock.
  - `Expand(booking, from, to)` turns a weekly Booking into concrete Occurrences, skipping Skipped dates and stopping at `UntilUtc`.
  - `FindClashes(candidate, others)` holds the ADR-0001 check. Open-ended weekly vs open-ended weekly is the tricky case: it checks two weeks past the last skipped date, which is enough because the pattern repeats.
- **`Scheduling/ChurchClock.cs`**: the only place that knows about Manila time (ADR-0004). DB stores UTC; pages show local.
- **`Bookings/BookingService.cs`**: every write to a Booking. `ApproveTrackedAsync` is the only path onto the schedule:
  1. begin transaction
  2. `SELECT ... FOR UPDATE` on the Room row (like `lockForUpdate()`), so two Admins approving clashing requests at once queue up
  3. re-run the clash check
  4. approve and end the replaced Booking if it's a change request
  5. commit

## 6. Tests

`tests/RoomBooking.Tests/ScheduleTests.cs`: 12 xUnit tests on `Schedule` and the Manila conversion. `[Fact]` is a test method; `Assert.True/Equal` work like PHPUnit's. They need no DB because the rules are pure functions. That's the payoff of keeping `Schedule` free of EF.

## 7. Tailwind without Node

`RoomBooking.csproj` has a `Tailwind` MSBuild target that runs before every build: it downloads the standalone binary once, then compiles `Styles/app.css`. MSBuild targets are roughly npm scripts hooked into `dotnet build`. Custom classes (`.btn`, `.field`, `.card`) are `@apply` shortcuts in `Styles/app.css`.

The "Every week" toggle on the booking form uses no JS. Tailwind's `group-has-[#weekly:checked]` compiles to CSS `:has()`, which shows the "Last meeting date" field when the radio is checked.

## Where to go next

- Add email (password reset, approval notices): implement `IEmailSender<ApplicationUser>` and register it in `Program.cs`.
- Deploy: a `Dockerfile` plus a managed Postgres. Set `ConnectionStrings__DefaultConnection`, `SeedAdmin__*` and `Church__PublicBaseUrl` as environment variables (double underscore = nesting).
- CI: a GitHub Actions workflow running `dotnet test`.
