# Church Room Booking

Church building has multiple rooms used weekly for Bible studies and meetings. People sometimes occupy empty rooms without booking, and rightful groups avoid confrontation. This app makes room ownership for a time slot visible and official.

## Glossary

**Room** — A bookable physical space in the church building. Has a name and optional description. Each Room has one printed QR code on its door. Rooms are deactivated, never deleted.

**QR code** — Printed code on a Room's door. Encodes the URL of that Room's **Room page**, keyed by the Room's numeric ID (`/rooms/{id}`) so renames don't break printed codes.

**Room page** — Public page (no login) for one Room. Shows current status ("Booked now: …" / "Free until …"), today's and upcoming schedule, and a "Request this room" action. Purpose: settle occupancy disputes at the door.

**Leader** — Person who leads a meeting or group. Logs in to request Rooms. Owns the Bookings they request.

**Group name** — Plain text on a Booking (e.g. "Youth Bible Study"). Not an entity; no members.

**Admin** — Person who approves or rejects Leaders' requests and can edit any Booking. Can also book Rooms directly. Uses the app mostly from a laptop.

**Booker** — Anyone who logs in: a Leader or an Admin. Only Bookers need accounts; viewing is public.

**Account status** — Pending → Approved (or Rejected) by Admin. A Pending or Rejected Booker cannot log in; the login page says the account is waiting for approval. (Viewing is public, so a Pending Booker loses nothing by not being logged in.)

**Booking** — A Leader's claim on a Room for a time slot. Either **one-off** (single date + start/end time) or **weekly** (day of week + start/end time, from a start date, open-ended until ended by Leader or Admin). Weekly is the only repeat pattern.

**Occurrence** — One concrete date of a Booking. A one-off Booking has one Occurrence; a weekly Booking has one per week.

**Skipped date** — A single date on which a weekly Booking does not happen (holiday, group cancels). The Room shows free for that Occurrence.

_Avoid_: "reservation", "event", "series" — use Booking / Occurrence.

**Request** — A Booking with status Pending. Not part of the schedule; does not appear as occupying the Room.

**Booking status** — Pending → Approved or Rejected (with reason). Pending → Withdrawn (by Leader). Approved → Cancelled (one-off) or Ended (weekly, from a date). Admin may edit a Pending Booking's Room/date/time before approving; Leader sees what changed.

**Change request** — A Request from a Leader to move an approved Booking (Room/day/time). Points at the Booking it replaces. On approval the old Booking ends and the new one takes over; until then the old one stays approved.

**Conflict** — Two Occurrences in the same Room whose times overlap. Approved Occurrences may never conflict (ADR-0001).

## Decisions

- Room page is public; schedule, group name and Leader name are visible to anyone who scans. (Q1)
- Bookings are one-off or weekly. Admin approves a weekly Booking once; it covers all future Occurrences. Skipped dates allowed. No biweekly/monthly until a real group needs it. (Q2)
- No conflicts in the schedule, ever. Admin resolves conflicts by adjusting a request or an approved Booking, then approving. No force-approve. See docs/adr/0001. (Q3/Q4)
- Admin adjusts by editing directly (request or approved Booking), then approves. No "send back to Leader" status. Reject always carries a reason. (Q5)
- Leader changes that free space (Skipped date, end weekly, cancel one-off, withdraw Request) need no approval. Changes that take or move space go through a Change request. All Leaders can see every Room's schedule. (Q6)
- Group is plain text on the Booking, one owning Leader per Booking; Admin can reassign owner. No groups/members entity. (Q7)
- All schedules are public (every Room, every approved Booking). Auth only for Bookers (Leader, Admin). (Q7)
- Admin's own Bookings are approved on creation but still must pass the conflict check (ADR-0001). (Q7, assumed)
- Leaders self-register (name, email, password); account is Pending until Admin approves it. First Admin seeded from config; Admin can promote a Leader to Admin. Email + password only. No self-service password reset until email sending exists; Admin sets a temp password. (Q8) Build note: Pending accounts are blocked at sign-in via Identity's IUserConfirmation, rather than logging in to a "waiting" screen. Same outcome, far less code.
- Stack: Blazor Web App static SSR + Tailwind (standalone CLI) + optional Alpine + EF Core + Identity, .NET 10 LTS, one project. See docs/adr/0002. (Q10)
- Database: PostgreSQL (local Homebrew 17), EF Core + Npgsql. Per-Room row lock during conflict check. See docs/adr/0003. (Q11)
- Times stored UTC (timestamptz), shown/entered in Asia/Manila. Weekly = first Occurrence + 7-day steps. No overnight Bookings. See docs/adr/0004. (Q12)
- Admin manages Rooms (name, description, active flag). No capacity/amenities/photos. Printable QR sheet page via QRCoder. (Q13) Rooms never booked can be hard-deleted (mistakes); booked ones only deactivated, enforced by FK Restrict.
- Defaults: 15-min time steps, no booking horizon, xUnit tests only for conflict check + weekly expansion, dev seeds first Admin + 3 Rooms, user makes all commits. (Q15)
