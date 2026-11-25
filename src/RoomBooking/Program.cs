using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Bookings;
using RoomBooking.Components;
using RoomBooking.Components.Account;
using RoomBooking.Data;
using RoomBooking.Endpoints;
using RoomBooking.Scheduling;

var builder = WebApplication.CreateBuilder(args);

// ---- Services (the DI container, like Laravel's service container) ----

builder.Services.AddRazorComponents(); // static SSR only (ADR-0002)

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Seed.AdminRole, policy => policy.RequireRole(Seed.AdminRole));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // Sign-in requires an Admin-approved account (see ApprovedAccountConfirmation).
        options.SignIn.RequireConfirmedAccount = true;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

builder.Services.AddScoped<IUserConfirmation<ApplicationUser>, ApprovedAccountConfirmation>();

// Re-read roles/password changes from the DB every minute, so "make Admin" or a reset takes effect quickly.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.AddSingleton<ChurchClock>();
builder.Services.AddScoped<BookingService>();

var app = builder.Build();

// ---- HTTP pipeline (middleware, in order) ----

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();
app.MapActionEndpoints();

await Seed.RunAsync(app.Services, app.Configuration, app.Environment.IsDevelopment());

app.Run();
