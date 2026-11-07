# Proposal: Reservas de cancha futbol 5 (first slice)

One-paragraph outcome: clients with a registered account can create a 60-minute football-5 reservation at ARS 40,000, view their own reservations, and cancel them; the Administrator (existing `Administrator` role, provisioned out-of-band) can list all reservations with filters and cancel any of them. A slot already reserved can never be double-booked. No payments in this slice; Mercado Pago confirmation (Choni) arrives later.

## Quick path

1. Client picks active court + date -> sees 60-minute start times derived from `CourtSchedule` minus `CourtClosure` blocks.
2. Client confirms one slot -> system creates one reservation (court + date + start + user, ARS 40,000).
3. Client checks "Mis reservas" / admin checks filtered list; either party cancels per rules below.

## Business problem

The club has courts, weekly schedules, and closure blocks but no reservation entity. Availability (`CourtAvailabilityService.GetAvailableStartTimesAsync`) currently answers "what hours are open" without knowing "what hours are already taken". Without a booking record, two clients can claim the same slot and staff has no list to operate from.

## Target users

| User | Goal |
|------|------|
| Client (registered, confirmed account) | Reserve a football-5 hour fast, see only my bookings, cancel when plans change. |
| Administrator (`AppRoles.Administrator`, provisioned via `AdminProvisioner --bootstrap-admin`, never self-registered) | See every reservation, filter by date / court / status / user, cancel on behalf of clients. |

Registration stays client-only. No new role system; reuse the existing role string.

## Product outcome

- A reservable hour becomes a first-class record: court + date + start time + owner + price + status.
- Double-booking is impossible: occupied slots disappear from availability and concurrent creates are rejected at DB level.
- Both sides can cancel; history stays queryable by status.

## Current gap (verified in code)

| Area | What exists | What is missing |
|------|-------------|-----------------|
| `Models/BookingPolicy.cs` | `Sport = "Football 5"`, `DurationMinutes = 60`, `PriceArs = 40_000` | No reservation entity consumes it. |
| `Services/CourtAvailabilityService.cs` | Expands schedules into 60-min slots, subtracts `CourtClosure` overlaps | Does not subtract reservations (no table to read). |
| `Data/ApplicationDbContext.cs` | `Courts`, `CourtSchedules`, `CourtClosures` + indexes | No `Bookings` DbSet, no unique slot index. |
| `Controllers/CourtsController.cs`, `AvailabilityController.cs` | Admin-gated (`[Authorize(Roles = Administrator)]`) with `AllowAnonymous` read paths | No booking endpoints; auth pattern to reuse. |
| `Program.cs` + `Security/AdminProvisioner.cs` | `AddDefaultIdentity` with `RequireConfirmedAccount = true`, one-time `--bootstrap-admin` that never promotes existing accounts | Registration flow already constrains who can book; nothing to change. |

## Business rules (ground truth for this slice)

1. Reservation = one active football-5 court + one 60-minute slot + one user, priced at ARS 40,000 (`BookingPolicy.PriceArs`).
2. Slots come from `CourtSchedule` day-of-week intervals; `CourtClosure` intervals block whole or partial slots (current overlap rule: `closure.StartTime < slotEnd && closure.EndTime > slotStart`).
3. Duplicate = a slot already reserved cannot be taken by anyone. Enforcement is two-layer: application check plus database unique constraint on `(CourtId, Date, StartTime)` for non-cancelled rows (or equivalent status-aware design decided in spec).
4. First-slice operations only: create, view mine, admin list (filters: date / court / status / user), status lookup, cancellation by owner and by admin.
5. Statuses: at minimum `Active` and `Cancelled`. Cancelled slots become bookable again; cancelled rows remain for history/audit.
6. Past slots cannot be booked; cancelled-then-past stays historical.
7. Only the owner or an admin can cancel a given reservation.
8. User-facing copy in Spanish (existing convention: "Selecciona una fecha válida.", "El horario se superpone...").

## Implications

| Topic | Decision |
|-------|----------|
| Slot source of truth | `CourtAvailabilityService` must subtract active reservations in addition to closures; otherwise double display. |
| Concurrency | Two simultaneous POSTs for the same slot: one wins, the other gets a friendly Spanish conflict message. Requires unique DB constraint, not just `AnyAsync` check. |
| Court deactivation | `CourtService` uses soft-deactivate (`IsActive = false`). Existing reservations on a deactivated court stay visible historically; new bookings on inactive courts are rejected (same as availability today). |
| Schedule/closure edits after booking | Changing hours or adding a closure must not silently delete reservations; spec decides whether to block edits that collide with active bookings or flag them for admin. |
| Identity | Booking owner = `IdentityUser.Id`. `RequireConfirmedAccount = true` means unconfirmed accounts cannot book. Admin identity unchanged. |
| Price | Fixed `40_000m` constant per reservation in this slice; no per-court pricing, no discounts, no payment state. |
| Migrations | Explicit EF Core migration for the new table + indexes; `**/*.db` is already git-ignored so `app.db` is never committed. |

## Edge cases

- Double-click / retry creates two POSTs -> idempotency via unique constraint; second request returns "already reserved" in Spanish.
- Cancelled slot rebooked -> allowed; only one active row per slot at a time.
- Booking on closure boundary (e.g. closure 10:30-11:30 vs slot 10:00-11:00) -> blocked by existing overlap rule.
- Inactive court, unknown court, schedule-less weekday -> no slots, booking rejected with 404 / Spanish validation.
- Owner cancels twice; admin cancels already-cancelled -> idempotent no-op with status feedback, not an error crash.
- Booking in the past due to clock skew -> date validation against server local date.
- Admin filters combine (e.g. court + date range + status + user) -> empty result is valid, not an error.

## First-slice boundaries (in scope)

- Create reservation (authenticated client).
- View my reservations.
- Admin list with filters (date / court / status / user).
- Status lookup (owner sees own; admin sees all).
- Cancellation by owner and by admin.

## Non-goals (explicitly out)

- Payments, deposits (seña), Mercado Pago callbacks — owned by Choni; this slice stores price only and coordinates later on "seña aprobada" confirmation.
- Recurring reservations, waiting lists, guest (non-registered) booking.
- New roles or permissions beyond `Administrator`.
- Per-court pricing, dynamic pricing, refunds.
- Notifications (email/SMS) on create/cancel.
- Schedule/closure conflict auto-resolution beyond the rule in Implications.

## Constraints

- Reuse `BookingPolicy` (60 min, football 5, ARS 40,000); do not hardcode duplicates.
- Reuse `CourtAvailabilityService` slot-expansion logic; extend, do not fork.
- Reuse `[Authorize(Roles = AppRoles.Administrator)]` + `AllowAnonymous` read pattern.
- Spanish user-facing copy; English code and artifacts.
- Explicit migrations; never commit `app.db` (`**/*.db` ignored).
- Admin provisioned outside the app (`--bootstrap-admin`); registration remains client-only.

## Tradeoffs

| Option | Chosen | Why |
|--------|--------|-----|
| Unique DB constraint + app check vs app check only | Both layers | App check gives friendly UX; constraint is the only real guarantee under concurrency (SQLite + EF). |
| Physical delete vs status column on cancel | Status column (`Cancelled`) | Preserves history for admin list/audit and makes rebooking logic explicit. Slightly more query care. |
| Availability subtracts bookings vs booking validates against static availability | Both | Display must hide taken slots; POST must revalidate (TOCTOU otherwise). Small duplication, worth it. |
| Fixed price constant vs price table | Constant | Correct for single-product slice; revisit when Choni's payment flow lands. |

## Checklist

- [ ] Reviewer can state what a reservation is without re-reading code.
- [ ] Reviewer can explain why double-booking needs both app and DB layers.
- [ ] Reviewer agrees payments are out and Choni's integration point is named.
- [ ] Reviewer agrees cancellation keeps history and frees the slot.

## Next step

`spec` phase: define the reservation entity, status model, endpoint contracts, filter semantics, and acceptance criteria. Do not write `design` or `tasks` yet.
