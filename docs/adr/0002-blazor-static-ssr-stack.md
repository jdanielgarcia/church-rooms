# 2. Blazor Web App with static SSR, Tailwind, EF Core, Identity on .NET 10

Date: 2026-11-25
Status: Accepted

## Context

Mobile-first: Leaders and passers-by open the Room page on phones at the door, often on weak church Wi-Fi. Admin uses a laptop. Project is also a .NET learning/portfolio piece for a developer coming from TALL (Tailwind, Alpine, Laravel, Livewire).

Options considered:
- Blazor Web App, static server rendering (chosen)
- Razor Pages (same model, older style)
- Web API + separate JS SPA (two projects, half the learning is JS)
- Blazor interactive server (Livewire look-alike, but holds a SignalR WebSocket and in-memory state per user; drops on flaky mobile connections)

## Decision

- One ASP.NET Core project on **.NET 10 LTS**, from `dotnet new blazor --auth Individual`.
- **Static SSR only**: pages render to HTML on the server, forms post back. No interactive render modes for now.
- **Tailwind** via the standalone CLI binary (no Node/npm).
- **Alpine.js** from CDN only where a page needs small client-side behaviour.
- **EF Core** for data access, **ASP.NET Core Identity** for Booker accounts.

## Consequences

- Works on any phone browser with no persistent connection.
- No live updates; pages show state as of load. Acceptable: schedules change rarely.
- Interactive server mode can be enabled per Admin component later without restructuring.
