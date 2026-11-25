# 3. PostgreSQL via EF Core (Npgsql)

Date: 2026-11-25
Status: Accepted

## Context

SQLite would cover church scale and serialize writes for free. PostgreSQL chosen instead for portfolio value and room to grow (multiple app instances, real hosting). Developer already runs PostgreSQL 17 locally via Homebrew.

## Decision

- PostgreSQL with `Npgsql.EntityFrameworkCore.PostgreSQL`.
- Local dev uses the Homebrew instance; connection string in user secrets, not committed.
- Race safety for ADR-0001: every write that can create an approved Occurrence (approve, Admin create, Admin edit) runs in a transaction that first locks the target Room row (`SELECT ... FOR UPDATE`), then runs the conflict check, then saves. Two Admins approving clashing Requests at once get serialized per Room.

## Consequences

- Needs a running Postgres for dev and tests.
- Per-Room lock means writes to different Rooms never wait on each other.
