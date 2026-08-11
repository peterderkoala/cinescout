# User table seeded with a null password hash, not created at setup time

CineScout's `User` table (see `../CONTEXT.md`) is seeded by migration with one row — a fixed `Username`, `PasswordHash = null` — rather than created by the first-run setup flow itself. `PasswordHash` being null is the sole first-run signal; setup performs an `UPDATE`, not an `INSERT`. This gives the row a stable `Id` before setup ever runs, and composes cleanly with `Username` being a fixed, seeded value rather than operator-chosen — an insert-on-setup design would otherwise need to guard a race on the unique `Username` index. The cost: `PasswordHash` and `SetupCompletedAt` (renamed from `CreatedAt`, since the row's existence no longer means "account created") must both be nullable, so downstream code can't treat a fetched `User` row as automatically fully set up.

## Considered Options

- **No row until setup completes** (rejected): `Users` starts empty; the setup flow inserts the one row with all fields required/non-null. Simpler nullability, but no stable `Id` before setup runs, and setup becomes an insert that has to be race-safe against the unique `Username` index.
