# 4. Store times in UTC, display in Asia/Manila

Date: 2026-11-25
Status: Accepted

## Context

Church is in the Philippines (Asia/Manila, UTC+8, no daylight saving). Considered storing wall-clock local `date` + `time`; chose UTC instants for consistency with standard practice.

Weekly Bookings must still mean "same local weekday and time every week". Storing a UTC weekday would be wrong for early-morning slots (Tue 07:00 Manila = Mon 23:00 UTC).

## Decision

- All instants stored as Postgres `timestamptz` (UTC). In C#: `DateTime` with `Kind = Utc`.
- A Booking stores the **first Occurrence's** start and end in UTC. Weekly Bookings generate later Occurrences by adding exactly 7 days. Optional end instant ("repeat until").
- A Skipped date stores the UTC start instant of the skipped Occurrence.
- Display and input converted with one configured time zone, `Asia/Manila`, from `appsettings.json`.
- No overnight Bookings: start and end fall on the same local (Manila) date, and end is after start.

## Consequences

- Adding 7 days in UTC is correct only because Manila has no DST. If the app ever serves a DST zone, weekly expansion must move to local time + zone conversion per Occurrence.
- Every form and page converts at the edge; domain logic works in UTC.
