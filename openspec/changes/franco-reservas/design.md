# Design: Slot-safe court reservations without double-booking

Clients with a confirmed account can reserve one 60-minute football-5 slot at ARS 40,000, see only their own bookings, and cancel them. The Administrator lists every booking with combined filters and cancels any of them. One active row per slot is enforced at the database level, so concurrent requests can never double-book.

## Quick path

1. Client posts court + date + start time to `POST /Bookings` -> `BookingService.CreateAsync` validates in spec order, inserts one `Active` row.
2. Taken slots disappear from `CourtAvailabilityService.GetAvailableStartTimesAsync` because it subtracts `Active` bookings after closures.
3. Owner checks `GET /Bookings/Mine`, owner or admin cancels via `POST .../Cancel`; cancel is idempotent and frees the slot for rebooking.
4. Verify with `dotnet test` following `tests/RuedaLaBocha.Tests` conventions (xUnit + `TestApp`, SQLite in-memory), including the migration snapshot test.

## Details

### Route layout

| Controller | Route | Auth | Purpose |
|------------|-------|------|---------|
| `BookingsController` (`Controllers/BookingsController.cs`, `[Authorize]`) | `POST /Bookings` | Authenticated, confirmed account | Create. Form or JSON (`courtId`, `date`, `startTime`). Success: `201 Created` (API) or redirect to `Mine` (form). Failures: `400` validation (Spanish), `404` unknown court, `409` slot taken. |
| `BookingsController` | `GET /Bookings/Mine?status=` | Owner | List caller's rows only, ordered by `Date`, `StartTime`. `status` optional (`Active` / `Cancelled` / omitted = all). `401` when anonymous. |
| `BookingsController` | `GET /Bookings/{id}` | Owner of `id` or admin | Status lookup. `403` non-owner non-admin, `404` unknown id. Returns court, date, start/end, price, status, owner. |
| `BookingsController` | `POST /Bookings/{id}/Cancel` | Owner of `id` | Owner cancel. Idempotent: repeat returns current `Cancelled` status with "ya estaba cancelada", not an error. `403` / `404` as above. |
| `AdminBookingsController` (`Controllers/AdminBookingsController.cs`, `[Authorize(Roles = AppRoles.Administrator)]`, `[Route("Admin/Bookings")]`) | `GET /Admin/Bookings?date=&courtId=&status=&userId=` | Administrator only | List all users' rows, filters combined with AND, empty result is `200` with empty list. `401` anonymous, `403` non-admin. |
| `AdminBookingsController` | `POST /Admin/Bookings/{id}/Cancel` | Administrator only | Cancel any owner's row. Same idempotent semantics. `404` unknown id. |

Reuse the existing `CourtsController` / `AvailabilityController` pattern: class-level `[Authorize(Roles = AppRoles.Administrator)]` for the admin controller, plain `[Authorize]` for the owner controller (Identity already enforces `RequireConfirmedAccount = true` before booking logic runs). No new roles, no endpoint creates or promotes administrators. Views follow the existing MVC form + redirect style; JSON responses use the same status codes for testability.

### BookingService shape

New `Services/BookingService.cs`, registered as `AddScoped<BookingService>()` in `Program.cs` next to `CourtService`. Controllers stay thin: map service outcomes and exceptions to status codes, select Spanish copy.

| Method | Signature (sketch) | Behavior |
|--------|-------------------|----------|
| Create | `Task<Booking> CreateAsync(string userId, int courtId, DateOnly date, TimeOnly startTime)` | Runs validation order below, inserts `Active` row with `PriceArs = BookingPolicy.PriceArs`, `EndTime = StartTime + 60 min`, `CreatedAtUtc = DateTime.UtcNow`. Throws `BookingValidationException` (400), `BookingNotFoundException` (404), `BookingConflictException` (409). Catches `DbUpdateException` from the filtered unique index and converts to `BookingConflictException`. |
| Mine | `Task<List<Booking>> MineAsync(string userId, BookingStatus? status)` | `Where(b => b.UserId == userId)`, optional status filter, `OrderBy(Date).ThenBy(StartTime)`. `AsNoTracking`. |
| Admin list | `Task<List<Booking>> AdminListAsync(BookingFilter filter)` | `filter` holds `DateOnly? Date`, `int? CourtId`, `BookingStatus? Status`, `string? UserId`. All optional, combined with AND. Unknown court/user id yields empty list. Invalid status value rejected at model binding (400). Ordered by `Date`, `StartTime`. `AsNoTracking`, `Include(b => b.Court)`. |
| Lookup | `Task<Booking?> GetForViewerAsync(int id, string viewerUserId, bool isAdmin)` | Returns row with `Include(b => b.Court)` if viewer is owner or admin, else null with a forbidden flag. Controller maps to `404` (unknown) vs `403` (non-owner). |
| Cancel | `Task<(Booking booking, bool wasAlreadyCancelled)> CancelAsync(int id, string callerUserId, bool isAdmin)` | Loads row. Throws `BookingNotFoundException` if missing, `BookingForbiddenException` if caller is neither owner nor admin. If `Status == Cancelled`, returns `(booking, true)` without writing. Otherwise sets `Status = Cancelled`, `CancelledAtUtc = DateTime.UtcNow`, saves. |

Supporting types in `Models/`: `Booking.cs`, `BookingStatus.cs` (`Active = 0`, `Cancelled = 1`), `BookingFilter.cs` (record with the four optional filters). Service exceptions in `Services/BookingExceptions.cs`: `BookingNotFoundException`, `BookingForbiddenException`, `BookingValidationException` (carries Spanish message), `BookingConflictException` (fixed Spanish message "Este horario ya fue reservado. Elegí otro horario disponible."). `EndTime` invariant (`EndTime - StartTime == 60 min`) enforced at creation from `BookingPolicy.DurationMinutes`; persisted as a column so queries and views do not recompute.

### EF mapping

```csharp
public enum BookingStatus { Active = 0, Cancelled = 1 }

public sealed class Booking
{
    public int Id { get; set; }
    public int CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public string UserId { get; set; } = "";
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal PriceArs { get; set; }
    public BookingStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
}
```

`ApplicationDbContext` additions:

```csharp
public DbSet<Booking> Bookings => Set<Booking>();

modelBuilder.Entity<Booking>(b =>
{
    b.Property(x => x.UserId).IsRequired().HasMaxLength(450);
    b.Property(x => x.PriceArs).HasColumnType("decimal(18,2)").HasPrecision(18, 2);
    b.HasOne(x => x.Court).WithMany().HasForeignKey(x => x.CourtId)
        .OnDelete(DeleteBehavior.Restrict);
    b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId)
        .OnDelete(DeleteBehavior.Restrict);
    // Exactly one Active row per slot; Cancelled rows never block rebooking.
    b.HasIndex(x => new { x.CourtId, x.Date, x.StartTime })
        .IsUnique()
        .HasFilter("Status = 0");
    b.HasIndex(x => new { x.UserId, x.Date, x.Status });
    b.HasIndex(x => new { x.Date, x.Status });
});
```

| Topic | Decision |
|-------|----------|
| FK delete behavior | `Restrict` on both `CourtId` and `UserId`. Deleting a court or user must never cascade-delete history; matches `CourtService` soft-deactivate precedent (`IsActive = false` keeps rows). |
| Filtered unique index | Partial index `WHERE Status = 0` (`Active`). SQLite supports partial indexes; EF `HasFilter("Status = 0")` emits `WHERE "Status" = 0` on SQLite and `[Status] = 0` on SQL Server. Enum stored as `int`, so the filter is provider-stable. Cancelled rows are excluded, rebooking a cancelled slot succeeds. |
| Secondary indexes | `(UserId, Date, Status)` serves `MineAsync`; `(Date, Status)` serves admin date/status filters. Both match spec ordering. |
| Money | `decimal(18,2)` via `HasColumnType` + `HasPrecision` so SQLite and future providers keep precision. Value always snapshots `BookingPolicy.PriceArs` at creation, never recomputed. |
| `UserId` length | `nvarchar(450)` / `TEXT` with max length 450 to match Identity key length. |

### Availability subtraction query

Extend `CourtAvailabilityService.GetAvailableStartTimesAsync`, do not fork it. Keep the existing schedule expansion and closure overlap rule (`closure.StartTime < slotEnd && closure.EndTime > slotStart`) untouched, then subtract active bookings with one extra query:

```csharp
var taken = await db.Bookings.AsNoTracking()
    .Where(b => b.CourtId == courtId && b.Date == date && b.Status == BookingStatus.Active)
    .Select(b => b.StartTime)
    .ToListAsync();
var takenSet = new HashSet<TimeOnly>(taken);
// inside the slot loop:
if (!blocked && !takenSet.Contains(slotStart)) slots.Add(slotStart);
```

One indexed query per availability call (`(Date, Status)` covers it; court filter uses the leading column of the unique index). `POST /Bookings` revalidates independently by checking membership in `GetAvailableStartTimesAsync` output plus an explicit `AnyAsync` for an `Active` row on `(CourtId, Date, StartTime)` — display check and write check are both required (TOCTOU otherwise).

### Validation order and concurrency mapping

`CreateAsync` evaluates in this order; first failure wins:

1. Court exists, else `BookingNotFoundException` -> `404` "La cancha solicitada no existe."
2. `Court.IsActive == true`, else `BookingValidationException` -> `400` "Esta cancha no está disponible para reservas."
3. `date >= DateOnly.FromDateTime(DateTime.Today)` (server local), else `400` "No se puede reservar un horario pasado. Elegí una fecha futura."
4. `startTime` is in `GetAvailableStartTimesAsync(courtId, date)` output (schedule-derived 60-minute grid minus closures minus active bookings), else `400` closure/schedule message ("Este horario no está disponible por un bloqueo de la cancha." or "Selecciona una fecha válida." for schedule-less days).
5. App-level `AnyAsync` for `Active` row on slot, else `BookingConflictException` -> `409` "Este horario ya fue reservado. Elegí otro horario disponible."
6. `SaveChangesAsync` inside try/catch: on `DbUpdateException` whose inner `SqliteException.SqliteExtendedErrorCode` / message indicates `UNIQUE constraint failed: Bookings...`, throw `BookingConflictException` with the same 409 message. Never leaks `500`. Check `ex.InnerException` message contains "UNIQUE" to stay provider-tolerant; add a comment naming the SQL Server equivalent (error 2601/2627) for the future provider.

Cancel is idempotent: cancelling a `Cancelled` row returns `(booking, wasAlreadyCancelled: true)`; controller renders "Esta reserva ya estaba cancelada." with `200`, not an error.

### Migration plan

1. Add entity + `OnModelCreating` config, then `dotnet ef migrations add AddBookings --project src/RuedaLaBocha.Web`.
2. Verify the migration contains `CreateTable Bookings` with `decimal(18,2)`, both FKs `onDelete: Restrict`, the filtered unique index (`filter: "Status = 0"`), and the two secondary indexes.
3. Update the snapshot test pattern from `CourtAvailabilityServiceTests.Migration_snapshot_matches_the_context_model`: reference the new migration name (`AddBookings`) and assert model, snapshot, and latest migration agree (empty diff both ways).
4. Never commit `*.db` (`**/*.db` is already git-ignored; tests use temp `tests.db` via `TestApp`).

### Collision policy for schedule/closure edits vs active bookings

Default: flag for admin, do not block. `CourtScheduleService` create/update and closure create/update keep their current overlap-against-each-other checks and gain no booking-collision veto in this slice. Rationale: blocking risks admin lockout (a booking far in the future would freeze hour maintenance), and silent deletion is already forbidden. Instead, the `Availability Manage` view queries overlapping `Active` bookings for the edited `(courtId, date/day, interval)` and renders a warning ("Esta edición solapa N reserva(s) activa(s); las reservas se conservan.") so staff can contact clients or cancel explicitly. Existing `Active` bookings stay valid and playable even if their slot is no longer on the grid; new bookings follow the new grid. A future slice may add an opt-in hard block once staff workflow is observed — that change needs its own spec because it alters admin throughput.

## Checklist

- [ ] Reviewer can trace `POST /Bookings` -> `CreateAsync` -> filtered unique index -> `409` Spanish message without re-reading code.
- [ ] Reviewer can state why two controllers (`Bookings` + `Admin/Bookings`) exist and which auth attribute each carries.
- [ ] Reviewer can explain why availability subtracts bookings and POST still revalidates.
- [ ] Reviewer agrees schedule/closure edits flag rather than block, and bookings are never silently deleted.
- [ ] Reviewer confirms migration + snapshot test pattern and no committed `*.db`.

## Next step

`tasks` phase: split into reviewable work units (entity + migration + snapshot test, service + availability subtraction, owner routes, admin routes, Spanish copy, concurrency test), each with its test from the spec acceptance criteria. Do not write code yet.
