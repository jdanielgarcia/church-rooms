# 1. No two approved Occurrences may overlap in the same Room

Date: 2026-11-25
Status: Accepted

## Context

App exists to end disputes over who owns a Room at a given time. If the schedule ever shows two groups in one Room at once, the Room page stops settling arguments and the app has failed at its one job.

Considered giving Admin a force-approve that bumps or double-books the existing Booking. Rejected: conflicts get resolved by people talking (offline), but the result must be recorded in the app, and the schedule must stay conflict-free.

## Decision

Hard invariant: **no two approved Occurrences in the same Room overlap in time.** Applies to everyone, Admin included.

- Leader cannot submit a request that overlaps an approved Occurrence.
- Pending requests may overlap each other; they are not part of the schedule. Approving one leaves the others pending but flagged as conflicting until Admin adjusts or rejects them.
- Admin cannot approve a request that would break the invariant. Admin first adjusts the request or the existing approved Booking (change time, change Room, add a Skipped date, end or cancel it), then approves.
- Checked in application code inside the same DB transaction as approval/edit, because weekly open-ended Bookings can't be expressed as a simple DB range constraint. Race safety via per-Room row lock (ADR-0003).

## Consequences

- Approve button disabled (with list of clashing Occurrences) while a conflict exists.
- Every Booking edit by Admin re-runs the same conflict check.
- One conflict-check function, used by request submit, approve, and edit. Main thing to unit test.
