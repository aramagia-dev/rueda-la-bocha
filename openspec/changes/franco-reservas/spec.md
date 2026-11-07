# Spec: Court reservations (first slice)

A reservation is one active football-5 court plus one 60-minute slot plus one user, priced at ARS 40,000. Clients create, view, and cancel their own reservations; the Administrator lists all reservations with filters and cancels any of them. A slot with an active reservation can never be double-booked.

## Quick path

1. Authenticated client with a confirmed account posts court + date + start time.
2. System validates the slot against schedule, closures, court status, and existing active reservations, then persists one `Active` reservation.
3. Owner views it under "Mis reservas"; owner or Administrator cancels it (`Cancelled`); a cancelled slot becomes bookable again.

## Reservation entity

### Fields

| Field | Type | Required | Rule |
|-------|------|----------|------|
| `Id` | `int`, identity | Yes | Primary key. |
| `CourtId` | `int`, FK to `Court` | Yes | Must reference an existing court. New bookings require `Court.IsActive == true`. |
| `Court` | Navigation to `Court` | Yes | Required navigation, non-null. |
| `UserId` | `string`, FK to `IdentityUser.Id` | Yes | Owner of the reservation. Max length 450 (Identity key length). Never null or empty. |
| `Date` | `DateOnly` | Yes | Calendar day of the slot, server-local. Must not be in the past at creation. |
| `StartTime` | `TimeOnly` | Yes | Slot start. Must align with a slot produced by `CourtAvailabilityService` slot expansion (schedule-derived, 60-minute grid). |
| `EndTime` | `TimeOnly`, derived | Yes | Always `StartTime + BookingPolicy.DurationMinutes` (60). Persisted or computed in design; spec requires the invariant `EndTime - StartTime == 60 min`. |
| `PriceArs` | `decimal(18,2)` | Yes | Snapshot of `BookingPolicy.PriceArs` (40,000) at creation. Never negative. |
| `Status` | Enum `BookingStatus` (`Active`, `Cancelled`) | Yes | New reservations are `Active`. Cancel transitions `Active -> Cancelled` only. |
| `CreatedAtUtc` | `DateTime`, UTC | Yes | Set once at creation. Used for audit and ordering. |
| `CancelledAtUtc` | `DateTime?`, UTC, nullable | No | Null while `Active`; set once on cancel. Never cleared. |

### Constraints

| Constraint | Scope | Purpose |
|------------|-------|---------|
| PK on `Id` | Table | Identity. |
| FK `CourtId -> Courts.Id`, delete `Restrict` | Table | Deleting a court must not cascade-delete reservation history. |
| FK `UserId -> AspNetUsers.Id`, delete `Restrict` | Table | Deleting a user must not cascade-delete reservation history. |
| Status-aware uniqueness on `(CourtId, Date, StartTime)` for `Active` rows only | Database | Exactly one active reservation per slot; cancelled rows do not block rebooking. Implemented as a filtered unique index (`WHERE Status = Active`) or equivalent status-aware design. |
| Index on `(UserId, Date, Status)` | Database | Supports "Mis reservas" ordering and filtering. |
| Index on `(Date, Status)` | Database | Supports admin date/status filters. |
| `decimal(18,2)` column type for `PriceArs` | Database | Money precision on SQLite and future providers. |

### Status model

| Status | Meaning | Transitions |
|--------|---------|-------------|
| `Active` | Holds the slot. Blocks other bookings and hides the slot from availability. | `Active -> Cancelled` (owner or Administrator). |
| `Cancelled` | Released the slot. Row retained for history and audit. Slot becomes bookable again. | Terminal. No transition out. Re-cancelling is an idempotent no-op returning current status, not an error. |

Cancelled-then-past rows remain queryable; they are never deleted in this slice.

## Endpoint contracts

All booking routes require authentication. Registration remains client-only; no endpoint creates or promotes administrators. `RequireConfirmedAccount = true` applies: unconfirmed accounts cannot book.

| Operation | Route shape | Auth | Input | Success | Failure |
|-----------|-------------|------|-------|---------|---------|
| Create reservation | `POST /Bookings` (form or JSON: `courtId`, `date`, `startTime`) | Authenticated user, confirmed account | `courtId: int`, `date: DateOnly`, `startTime: TimeOnly` | `201 Created` (or MVC redirect to "Mis reservas" on form post) with the new reservation id, slot, price, and `Active` status | `400` + Spanish message on validation failure; `404` on unknown court; `409` on already-reserved slot |
| View my reservations | `GET /Bookings/Mine` | Owner (authenticated) | Optional `status` query (`Active` / `Cancelled` / omitted = all) | `200` with only the caller's rows, ordered by `Date` then `StartTime` | `401` when anonymous |
| Admin list with filters | `GET /Admin/Bookings?date=&courtId=&status=&userId=` | `Administrator` role only | Optional filters (see Filter semantics) | `200` with matching rows across all users, ordered by `Date` then `StartTime` | `401` anonymous; `403` non-admin authenticated |
| Status lookup | `GET /Bookings/{id}` | Owner of `id` or `Administrator` | `id: int` route | `200` with reservation detail (court, date, start/end, price, status, owner) | `401` anonymous; `403` non-owner non-admin; `404` unknown id |
| Cancel by owner | `POST /Bookings/{id}/Cancel` | Owner of `id` | `id: int` route | `200` (or redirect) with `Cancelled` status; idempotent on repeat | `401` anonymous; `403` non-owner; `404` unknown id |
| Cancel by admin | `POST /Admin/Bookings/{id}/Cancel` | `Administrator` role only | `id: int` route | `200` (or redirect) with `Cancelled` status; works on any owner's row; idempotent on repeat | `401` anonymous; `403` non-admin; `404` unknown id |

### Auth rules

| Rule | Detail |
|------|--------|
| Client-only registration | No change to `Program.cs` identity setup or `AdminProvisioner`. No endpoint assigns the `Administrator` role. |
| Administrator reuse | Admin-only operations use the existing `[Authorize(Roles = AppRoles.Administrator)]` pattern. The `Administrator` role string is reused, never duplicated. |
| Ownership | Booking owner is the authenticated `IdentityUser.Id` at creation. Owner-only reads and cancels compare caller id against `UserId`. |
| Confirmed account | `RequireConfirmedAccount = true` already enforced by Identity; unauthenticated or unconfirmed callers are rejected before booking logic runs. |

### Validation (create)

Evaluated in this order; first failure wins:

1. Court exists; otherwise `404`.
2. Court is active (`IsActive == true`); otherwise `400`.
3. Date is not in the past relative to the server local date; otherwise `400`.
4. `StartTime` is a valid bookable slot for that court and date: derived from the `CourtSchedule` day-of-week intervals with 60-minute expansion, minus `CourtClosure` overlaps using the existing rule (`closure.StartTime < slotEnd && closure.EndTime > slotStart`), minus existing `Active` reservations; otherwise `400`.
5. No existing `Active` reservation for `(CourtId, Date, StartTime)` (application check); otherwise `409`.
6. Database filtered unique constraint enforces rule 5 under concurrency; constraint violation maps to `409`, never a `500`.

New bookings on inactive courts, unknown courts, schedule-less weekdays, closure-blocked slots, and past slots are all rejected. `CourtAvailabilityService.GetAvailableStartTimesAsync` must subtract `Active` reservations in addition to closures so taken slots never display as free; the POST revalidates independently (display check and write check are both required).

### Duplicate prevention

Two layers, both mandatory:

| Layer | Behavior |
|-------|----------|
| Application check | Before insert, query for an existing `Active` row on `(CourtId, Date, StartTime)`. On hit, return `409` with a Spanish conflict message. Gives friendly UX for double-click and retry. |
| Database constraint | Filtered unique index on `(CourtId, Date, StartTime)` where `Status = Active` (or equivalent). On violation under concurrent POSTs, catch and return `409` with the same Spanish conflict message. This layer is the real guarantee; the app check alone is insufficient. |

Rebooking a `Cancelled` slot is allowed: only one `Active` row per slot may exist at a time.

### Filter semantics (admin list)

| Filter | Type | Semantics |
|--------|------|-----------|
| `date` | `DateOnly?` (single day; design may add `from`/`to` range) | Exact-day match. If a range is offered, bounds are inclusive. |
| `courtId` | `int?` | Exact court match. Unknown court id yields an empty list, not an error. |
| `status` | `BookingStatus?` | Exact status match. Invalid status value yields `400`. |
| `userId` | `string?` | Exact owner match. Unknown user id yields an empty list. |

Filters combine with AND. All filters are optional; no filters returns all rows. An empty result set is valid (`200` with an empty list), never an error. Default ordering is `Date` ascending, then `StartTime` ascending.

## Spanish UX copy examples

| Situation | Message |
|-----------|---------|
| Slot already reserved | "Este horario ya fue reservado. Elegí otro horario disponible." |
| Past slot | "No se puede reservar un horario pasado. Elegí una fecha futura." |
| Inactive court | "Esta cancha no está disponible para reservas." |
| Unknown court | "La cancha solicitada no existe." |
| Closure overlap | "Este horario no está disponible por un bloqueo de la cancha." |
| Cancel success | "Tu reserva fue cancelada. El horario quedó libre nuevamente." |
| Already cancelled (idempotent) | "Esta reserva ya estaba cancelada." |
| Forbidden (non-owner) | "Solo podés ver o cancelar tus propias reservas." |

Existing conventions (`"Selecciona una fecha válida."`, `"El horario se superpone..."`) are reused where applicable.

## Out of scope

Payments, deposits (seña), Mercado Pago callbacks (Choni owns that integration; this slice stores price only). Recurring reservations, waiting lists, guest booking without a registered account. New roles or permissions beyond `Administrator`. Per-court or dynamic pricing, discounts, refunds. Notifications on create or cancel. Automatic resolution of schedule or closure edits that collide with existing active bookings (those edits must not silently delete reservations; the collision policy is a design decision, defaulting to flagging for admin unless design justifies blocking).

## Acceptance criteria

Verifiable with `dotnet test` following existing `tests/RuedaLaBocha.Tests` conventions (xUnit + `TestApp`, SQLite in-memory):

- [ ] Authenticated client creates a reservation on a free slot and receives the slot, price 40,000, and `Active` status.
- [ ] Created reservation hides its slot from `GetAvailableStartTimesAsync` for that court and date.
- [ ] Second create (same court, date, start) returns a conflict in Spanish at the app layer.
- [ ] Concurrent duplicate creates leave exactly one `Active` row; the loser receives a conflict, never a `500`.
- [ ] Cancelling an `Active` reservation sets `Cancelled` with a timestamp and frees the slot for rebooking.
- [ ] Rebooking a cancelled slot succeeds; only one `Active` row exists per slot afterward.
- [ ] Cancelling an already-`Cancelled` reservation is an idempotent no-op reporting the current status.
- [ ] Past dates, inactive courts, unknown courts, schedule-less weekdays, and closure-overlapped slots are all rejected with the specified status codes and Spanish messages.
- [ ] "Mis reservas" returns only the caller's rows; another user's rows are never visible.
- [ ] Status lookup by non-owner non-admin returns forbidden; by owner or admin returns the detail; unknown id returns not found.
- [ ] Admin list filters by date, court, status, and user combine with AND; empty results return `200` with an empty list.
- [ ] Non-admin callers cannot reach admin list or admin cancel (forbidden); anonymous callers are challenged.
- [ ] Migration snapshot test passes: model, snapshot, and latest migration agree (same pattern as `CourtAvailabilityServiceTests.Migration_snapshot_matches_the_context_model`).
- [ ] No committed `*.db` files; `BookingPolicy` constants (60 min, ARS 40,000) are reused, not hardcoded.

## Next step

`design` phase: choose the `BookingsController`/`Admin` route layout, the `BookingService` shape, the filtered-index migration, and the availability subtraction query. Do not write tasks or code yet.
