# Tasks: franco-reservas (slot-safe court reservations)

Single-PR delivery. Work units below are reviewable commits inside one PR; each has start, finish, verification, rollback. Do NOT implement code in this phase.

Conventions: `dotnet test` from repo root (or `dotnet test tests/RuedaLaBocha.Tests`). Tests follow `tests/RuedaLaBocha.Tests` patterns (xUnit + `TestApp`, SQLite in-memory). Spanish user copy, English code. Never commit `*.db`.

## Task 1 — Booking entity + EF mapping + migration + snapshot test

Start: `Data/ApplicationDbContext.cs` has `Courts`, `CourtSchedules`, `CourtClosures`; no `Bookings` set.
Finish: `Booking` + `BookingStatus` + `BookingFilter` persisted with filtered unique index; `AddBookings` migration applied; snapshot test green.

Files:
- Create: `src/RuedaLaBocha.Web/Models/Booking.cs`, `src/RuedaLaBocha.Web/Models/BookingStatus.cs`, `src/RuedaLaBocha.Web/Models/BookingFilter.cs`
- Edit: `src/RuedaLaBocha.Web/Data/ApplicationDbContext.cs` (DbSet + `OnModelCreating` per design)
- Create (generated): `src/RuedaLaBocha.Web/Data/Migrations/*_AddBookings.cs` + snapshot update via `dotnet ef migrations add AddBookings --project src/RuedaLaBocha.Web`
- Edit: `tests/RuedaLaBocha.Tests/CourtAvailabilityServiceTests.cs` (or new `BookingMigrationTests.cs`) — extend snapshot pattern `Migration_snapshot_matches_the_context_model` to cover `AddBookings`

Mapping details (from design, no drift):
- `UserId` required, max length 450; `PriceArs decimal(18,2)` + precision; FK `CourtId` and `UserId` `Restrict`; filtered unique index `(CourtId, Date, StartTime)` `HasFilter("Status = 0")`; secondary indexes `(UserId, Date, Status)` and `(Date, Status)`; `BookingStatus.Active = 0, Cancelled = 1`; reuse `BookingPolicy.PriceArs` / `DurationMinutes`, no hardcode.

Tests (`dotnet test`):
- Snapshot test: model, snapshot, and latest migration agree (empty diff both ways).
- Entity invariant test: `EndTime - StartTime == 60 min`; `PriceArs` snapshots `BookingPolicy.PriceArs`; `CancelledAtUtc` null while Active.

Done criteria:
- [ ] `dotnet ef migrations add AddBookings` output reviewed: `CreateTable Bookings` with `decimal(18,2)`, both FKs `Restrict`, filtered unique index + 2 secondary indexes.
- [ ] Snapshot test passes.
- [ ] No `*.db` committed (`git status` clean of `*.db`).
- [ ] Spec criteria mapped: #13 (snapshot agree), #14 (no `*.db`, BookingPolicy reused).

Rollback: delete `AddBookings` migration files + revert `ApplicationDbContext.cs` + remove the three new Model files. No other code depends on `Booking` yet.

## Task 2 — BookingService + availability subtraction + exception mapping

Start: Task 1 merged in working tree; `Services/CourtAvailabilityService.cs` subtracts only closures; no `BookingService`.
Finish: `BookingService` implements create/mine/admin-list/lookup/cancel per design; availability hides taken slots; controllers can map exceptions to 400/404/409.

Files:
- Create: `src/RuedaLaBocha.Web/Services/BookingService.cs`, `src/RuedaLaBocha.Web/Services/BookingExceptions.cs` (`BookingNotFoundException`, `BookingForbiddenException`, `BookingValidationException`, `BookingConflictException` with fixed Spanish conflict message)
- Edit: `src/RuedaLaBocha.Web/Services/CourtAvailabilityService.cs` (add one `Active`-bookings query + `takenSet` subtraction; keep closure rule untouched)
- Edit: `src/RuedaLaBocha.Web/Program.cs` (`AddScoped<BookingService>()` next to `CourtService`)
- Edit: `tests/RuedaLaBocha.Tests/...` — new `BookingServiceTests.cs`: service-level create/conflict/cancel/rebook tests

Behavior contract:
- `CreateAsync(userId, courtId, date, startTime)`: validation order 1–6 from spec/design; `PriceArs = BookingPolicy.PriceArs`, `EndTime = StartTime + 60`, `CreatedAtUtc = UtcNow`; catch `DbUpdateException` with `UNIQUE constraint failed: Bookings` → `BookingConflictException` (comment SQL Server 2601/2627 equivalent).
- `MineAsync`, `AdminListAsync(BookingFilter)`, `GetForViewerAsync`, `CancelAsync` (idempotent, returns `wasAlreadyCancelled`) exactly per design signatures.
- Availability: `GetAvailableStartTimesAsync` subtracts `Active` bookings after closures; POST revalidates via availability membership + explicit `AnyAsync` (TOCTOU coverage).

Tests (`dotnet test tests/RuedaLaBocha.Tests --filter BookingService` then full `dotnet test`):
- Create on free slot → Active, price 40,000.
- Second create same slot → `BookingConflictException` with "Este horario ya fue reservado. Elegí otro horario disponible."
- Availability after create hides slot; cancel frees it; rebook succeeds with single Active row.
- Validation order: unknown court → NotFound; inactive court / past date / closure overlap / schedule-less day → Validation with specified Spanish messages.

Done criteria:
- [ ] All `BookingServiceTests` pass; full `dotnet test` green.
- [ ] Availability regression tests still pass (closure rule unchanged).
- [ ] Spec criteria mapped: #1 (create), #2 (availability hides), #3 (app-layer conflict), #8 partial (validation mapping at service level).

Rollback: remove `BookingService.cs` + `BookingExceptions.cs`, revert `CourtAvailabilityService.cs` and `Program.cs` registration. Controllers not yet wired, so no dangling references.

## Task 3 — Owner routes + views + Spanish copy

Start: `BookingService` available, no booking endpoints.
Finish: authenticated owner can create, list mine, lookup own, cancel own via MVC + JSON-testable status codes; Spanish copy per spec table.

Files:
- Create: `src/RuedaLaBocha.Web/Controllers/BookingsController.cs` (`[Authorize]`; `POST /Bookings`, `GET /Bookings/Mine?status=`, `GET /Bookings/{id}`, `POST /Bookings/{id}/Cancel`)
- Create/Edit views: `src/RuedaLaBocha.Web/Views/Bookings/Mine.cshtml` (+ create form view reusing existing MVC form + redirect style, e.g. `Views/Bookings/Create.cshtml` or Availability-page partial — pick one, document choice)
- Edit: `tests/...` — new `BookingOwnerFlowTests.cs` (TestApp: register + confirm account, create court + schedule fixture, POST/GET/cancel flows, ownership isolation)

Controller mapping:
- `BookingValidationException` → 400 + Spanish message; `BookingNotFoundException` → 404 "La cancha solicitada no existe." / unknown id; `BookingConflictException` → 409 conflict message; `BookingForbiddenException` → 403 "Solo podés ver o cancelar tus propias reservas."; cancel success → 200/redirect "Tu reserva fue cancelada. El horario quedó libre nuevamente."; repeat cancel → 200 "Esta reserva ya estaba cancelada."
- `Mine` returns only caller rows ordered by `Date`, `StartTime`; anonymous → 401 challenge.

Tests (`dotnet test --filter BookingOwnerFlow` then full suite):
- Owner create → 201/redirect with slot + price + Active; visible in Mine.
- Non-owner cannot read/cancel another's row (403); unknown id → 404.
- Double cancel → idempotent 200, still single Cancelled row with timestamp.
- Past/inactive/unknown/closure/schedule-less rejections carry specified codes + Spanish messages.

Done criteria:
- [ ] Owner flow tests green; `Mine` isolation proven (user B sees zero of user A's rows).
- [ ] Spanish copy matches spec table verbatim.
- [ ] Spec criteria mapped: #1, #5, #6, #7, #8, #9, #10 (owner half).

Rollback: delete `BookingsController.cs` + its views + `BookingOwnerFlowTests.cs`. Service and entity remain, unused by routes.

## Task 4 — Admin routes + filters + auth

Start: owner routes exist; no `/Admin/Bookings` endpoints.
Finish: Administrator lists all rows with AND-combined optional filters and cancels any row; non-admin forbidden, anonymous challenged.

Files:
- Create: `src/RuedaLaBocha.Web/Controllers/AdminBookingsController.cs` (`[Authorize(Roles = AppRoles.Administrator)]`, `[Route("Admin/Bookings")]`; `GET /Admin/Bookings?date=&courtId=&status=&userId=`, `POST /Admin/Bookings/{id}/Cancel`)
- Create/Edit views: `src/RuedaLaBocha.Web/Views/AdminBookings/Index.cshtml` (filter form + table ordered by Date, StartTime; reuse admin layout)
- Edit: `tests/...` — new `BookingAdminFlowTests.cs` (admin fixture via existing provisioner/test role helper; filter-combination, auth-matrix tests)

Semantics:
- Filters optional, AND-combined; unknown court/user id → 200 empty list; invalid status → 400; default order Date, StartTime.
- Admin cancel works on any owner's row, idempotent; 404 unknown id; 401 anonymous / 403 non-admin for both list and cancel.
- Reuse `AppRoles.Administrator` string; no new roles; schedule/closure edits unchanged (flag-don't-block policy: Manage view warning only — implement warning text in this task if the Manage view is touched, otherwise record as follow-up with file name).

Tests (`dotnet test --filter BookingAdminFlow` then full suite):
- Filter matrix: date × court × status × user AND-combination incl. empty-result 200.
- Auth matrix: anonymous 401, non-admin 403, admin 200 on list + cancel.
- Admin cancels another owner's Active → Cancelled; repeat → idempotent 200.

Done criteria:
- [ ] Admin flow tests green.
- [ ] No new role strings; `AppRoles.Administrator` reused.
- [ ] Spec criteria mapped: #10 (admin half), #11 (admin filters + empty 200), #12 (auth matrix).

Rollback: delete `AdminBookingsController.cs` + admin views + `BookingAdminFlowTests.cs`. Owner routes unaffected.

## Task 5 — Concurrency + idempotency + validation + acceptance sweep (all 14 criteria mapped)

Start: all routes wired; individual flows green.
Finish: concurrent duplicates provably safe; idempotency proven; full 14-criterion acceptance evidence recorded.

Files:
- Edit: `tests/RuedaLaBocha.Tests/BookingServiceTests.cs` or new `BookingConcurrencyTests.cs` — parallel `CreateAsync` race (e.g. `Task.WhenAll` × N on same slot, SQLite in-memory or separate TestApp scopes) asserting exactly one Active row + losers get conflict, never 500.
- Edit: `tests/...` — past/inactive/unknown/schedule-less/closure rejection parametrized `[Theory]`; double-cancel idempotency; `Mine` isolation; lookup 403/404; admin auth matrix (fill any gaps from Tasks 3–4).
- No `src/` changes expected unless a race surfaces a missing catch (then fix in `BookingService.CreateAsync` and note it).

Acceptance map (all 14, check each with test name):
1. Create free slot → slot + 40,000 + Active — `BookingOwnerFlowTests.Create_returns_Active_with_price`.
2. Availability hides taken slot — `BookingServiceTests.Availability_excludes_active_booking`.
3. App-layer duplicate → Spanish 409 — `...Duplicate_returns_conflict_in_Spanish`.
4. Concurrent duplicates → one Active, loser conflict, no 500 — `BookingConcurrencyTests.Parallel_creates_leave_single_active`.
5. Cancel Active → Cancelled + timestamp + slot freed — `...Cancel_frees_slot`.
6. Rebook cancelled → succeeds, single Active — `...Rebook_cancelled_succeeds`.
7. Re-cancel → idempotent no-op — `...Recancel_is_idempotent`.
8. Past/inactive/unknown/schedule-less/closure rejected with codes + Spanish — `...Validation_theory` (5+ cases).
9. Mine returns only caller rows — `...Mine_is_owner_scoped`.
10. Lookup owner/admin OK, non-owner 403, unknown 404 — `...Lookup_auth_matrix`.
11. Admin filters AND-combine, empty → 200 empty — `...Admin_filter_matrix`.
12. Non-admin 403 / anonymous 401 on admin routes — `...Admin_auth_matrix`.
13. Migration snapshot agree — Task 1 snapshot test name.
14. No `*.db` + BookingPolicy reused — `git status --porcelain -- '*.db'` empty + grep `40_000`/`40000` outside `BookingPolicy`/`BookingService` returns nothing (record commands + output).

Tests: full `dotnet test` must pass. Record exact command + result (e.g. `dotnet test → Passed: N, Failed: 0`).

Done criteria:
- [ ] Concurrency test passes repeatedly (run 3× or with `--blame-hang` note if flaky).
- [ ] All 14 boxes checked with test names above.
- [ ] Evidence appendix in PR description: focused + full test commands with exact results; runtime harness N/A (no runtime boundary — MVC verified via TestApp integration tests) or scenario recorded.

Rollback: tests-only task — revert test files. If a `src/` race fix was needed, its rollback is the `try/catch DbUpdateException → BookingConflictException` hunk in `BookingService.cs` (state exact lines in PR).

---

## Review Workload Forecast

| Signal | Estimate |
|--------|----------|
| New files | ~10 (3 models, 1 service, 1 exceptions, 2 controllers, 2–3 views, 3–5 test files; migration files generated) |
| Edited files | ~5 (DbContext, CourtAvailabilityService, Program, snapshot test, maybe Availability Manage view warning) |
| Estimated changed lines (authored, excl. generated migration/snapshot) | ~1100–1500 (entity+mapping ~120, service+exceptions ~300, availability delta ~30, owner controller+views ~250, admin controller+view ~200, tests ~400–600) |
| Generated lines (migration + snapshot, excluded from 400-count per skill but still reviewed) | ~200–400 |
| Total review surface | ~1300–1900 lines against the 2000-line session budget |
| Standard 400-line budget risk | HIGH — single PR exceeds the standard 400-line work-unit budget by ~3×; reviewer fatigue likely on Tasks 2–5 combined |
| Chained PRs recommended | No (per `delivery_strategy: single-pr`) — keep ONE PR with 5 work-unit commits; reviewer reads commit-by-commit |
| Decision needed before apply | Yes — confirm `size:exception` for single-PR over-budget delivery (standard 400 exceeded, session 2000 respected). If declined, alternative is 3 chained PRs: (1) Task 1, (2) Tasks 2–3, (3) Tasks 4–5. |
| `size:exception` required | YES — forecast >400 authored lines in one PR |

Work-unit commit plan (one commit per task, tests with code, story order):
1. `feat(bookings): add Booking entity, mapping, AddBookings migration and snapshot test`
2. `feat(bookings): add BookingService with availability subtraction and conflict mapping`
3. `feat(bookings): add owner booking routes, views and Spanish copy`
4. `feat(bookings): add admin booking list, filters and auth`
5. `test(bookings): add concurrency, idempotency and acceptance sweep for 14 criteria`
