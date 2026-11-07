# Verify report: franco-reservas (slot-safe court reservations)

- Date: 2026-11-07
- Verifier: sdd-verify subagent (validation only — no code changed, no PR opened)
- Commits reviewed: f412784, 50985cf, 9d382aa, 66acfe7, 74d0245 (5 work-unit commits, single-PR delivery)
- Test commands (exact):
  - `dotnet test --nologo` → `Correctas! - Con error: 0, Superado: 61, Omitido: 0, Total: 61, Duración: 1 m 15 s`
  - `dotnet test --nologo --filter 'FullyQualifiedName~BookingConcurrency'` → 1/1 passed, 3 consecutive runs (5 s, 5 s, 6 s)
- Diff surface: 23 files, 1625 insertions, 6 deletions (session budget 2000 — respected)

## Verdict: CLEAN — ready for archive

No CRITICAL findings. All 14 acceptance criteria PASS with named tests. Two WARNINGs (both justified, documented below) and three SUGGESTIONs (non-blocking follow-ups).

## Acceptance criteria map (14/14 PASS)

| # | Criterion | Result | Test evidence |
|---|-----------|--------|---------------|
| 1 | Create on free slot → slot + price 40,000 + Active | PASS | `BookingServiceTests.Create_on_free_slot_returns_active_with_policy_price` (asserts `BookingPolicy.PriceArs`, `EndTime = 10:00`, `CancelledAtUtc` null); `BookingOwnerFlowTests.Owner_create_lists_and_cancels_with_spanish_copy` (redirect + Mine shows `09:00` + `40.000`) |
| 2 | Created reservation hides slot from `GetAvailableStartTimesAsync` | PASS | `BookingServiceTests.Availability_hides_active_booking_and_cancel_frees_it` (Contains → DoesNotContain after create) |
| 3 | Second create same slot → Spanish 409 at app layer | PASS | `BookingServiceTests.Duplicate_create_returns_spanish_conflict` (exact message `"Este horario ya fue reservado. Elegí otro horario disponible."`); controller maps `BookingConflictException` → 409 (`Controllers/BookingsController.cs:36-39`) |
| 4 | Concurrent duplicates → one Active row, loser conflict, never 500 | PASS | `BookingConcurrencyTests.Parallel_creates_leave_single_active_and_spanish_conflicts` (8 parallel: 1 created / 7 conflict, exact Spanish message asserted on every loser, `COUNT(*) WHERE Status = 0 == 1`); stable 3/3 runs |
| 5 | Cancel Active → Cancelled + timestamp + slot freed | PASS | `Availability_hides_active_booking_and_cancel_frees_it` (`wasAlready == false`, `CancelledAtUtc` not null, slot reappears); `Admin_cancels_any_row_idempotently` (`Status == 1` in DB) |
| 6 | Rebook cancelled → succeeds, single Active row | PASS | `Availability_hides_active_booking_and_cancel_frees_it` (rebook after cancel → Active, `COUNT(*) WHERE Status = 0 == 1`) |
| 7 | Re-cancel → idempotent no-op with current status | PASS | `Owner_create_lists_and_cancels_with_spanish_copy` (repeat → 200 `"ya estaba cancelada"`); `Admin_cancels_any_row_idempotently` (repeat → 200 same message) |
| 8 | Past/inactive/unknown/schedule-less/closure rejected with codes + Spanish | PASS | `BookingServiceTests.Create_rejects_invalid_slots_with_spanish_messages` (5-case Theory, exact messages incl. `"La cancha solicitada no existe."` → NotFound); `BookingOwnerFlowTests.Owner_create_rejections_carry_status_and_spanish_message` (HTTP: past/inactive → 400, unknown → 404, closure → 400, message fragments) |
| 9 | Mine returns only caller's rows | PASS | `BookingOwnerFlowTests.Non_owner_cannot_read_or_cancel_and_unknown_id_is_not_found` (`otherMine` lacks owner's `09:00` row) |
| 10 | Lookup: owner/admin OK, non-owner 403, unknown 404 | PASS | Same test (owner 200, non-owner 403 + `"propias reservas"`, unknown id 404). Admin bypass via `User.IsInRole(AppRoles.Administrator)` in `Detail` — see SUGGESTION-1 |
| 11 | Admin filters AND-combine; empty → 200 empty | PASS | `BookingAdminFlowTests.Admin_list_filters_combine_and_empty_result_is_200` (date/court/user/combined filters, `courtId=999999` → 200 without rows, `status=Bogus` → 400) |
| 12 | Non-admin 403 / anonymous 401 on admin routes | PASS | `BookingAdminFlowTests.Admin_auth_matrix_challenges_anonymous_and_denies_customer` (anonymous → redirect Login, customer list + cancel → redirect AccessDenied; cookie-auth 302 manifestation of 401/403 — standard MVC convention) |
| 13 | Migration snapshot agrees | PASS | `CourtAvailabilityServiceTests.Migration_snapshot_matches_the_context_model` (pinned to `20261107001800_AddBookings`, asserts empty diff snapshot↔model AND migration-target↔model; green in full suite) |
| 14 | No `*.db` + `BookingPolicy` reused, not hardcoded | PASS | `git status --porcelain -- '*.db'` → empty; `Select-String 40_000|40000` over `src/` → only hit is `Models/BookingPolicy.cs:7` (the constant definition); `BookingService.cs:44-45` uses `BookingPolicy.DurationMinutes` / `BookingPolicy.PriceArs`; availability delta uses `BookingPolicy.DurationMinutes` (`CourtAvailabilityService.cs:31`); views use `BookingPolicy.PriceDisplay` / `DurationMinutes` |

## Findings

### CRITICAL — none

### WARNING-1: Validation order deviates from spec (steps 4↔5 swapped) — JUSTIFIED, keep

- Spec (`spec.md:76-81`) orders slot-validity check (4) before the application duplicate check (5).
- Implementation (`Services/BookingService.cs:24-36`) runs the explicit `Active`-duplicate `AnyAsync` check FIRST, then the availability-membership check.
- Why this is correct: `GetAvailableStartTimesAsync` now subtracts `Active` bookings, so a literal spec order would reject a duplicate slot at step 4 with 400 `BlockedSlotMessage` and step 5's 409 would be unreachable — contradicting criterion #3. Duplicate-first is the only order satisfying both #2 (subtraction) and #3 (409). Documented in apply memory (`sdd/franco-reservas/apply-progress`, observation #181).
- Action: amend spec validation order (swap 4↔5 with one-line rationale) in a follow-up edit, or accept as-is. Not a merge blocker.

### WARNING-2: 401/403 manifest as 302 redirects (cookie auth) — EXPECTED, keep

- Spec tables say `401`/`403`; MVC cookie authentication converts challenges/forbids to 302 → `/Identity/Account/Login` / `/AccessDenied`. Tests assert the redirect targets explicitly (`Anonymous_is_challenged_on_owner_routes`, `Admin_auth_matrix_...`). This matches existing project conventions (`CourtFlowTests`, `AvailabilityFlowTests` use the same pattern). No action needed; recorded so reviewers don't misread the tests.

### SUGGESTION-1: No HTTP test for admin viewing another owner's detail

- `GetForViewerAsync(id, viewer, isAdmin: true)` bypass is covered only via the owner controller's `User.IsInRole` wiring (code-read, `BookingsController.cs:62`). Service-level and owner/admin-cancel paths are tested; the admin-detail HTTP path is a two-line branch with no dedicated test. Optional: one assertion in `BookingAdminFlowTests` (admin GET `/Bookings/{id}` of another user's row → 200). Non-blocking.

### SUGGESTION-2: `Mine?status=Bogus` / `Admin?status=Bogus` share the generic `"Selecciona una fecha válida."` 400 message

- Spec requires 400 for invalid admin status (message unspecified) and is silent on invalid Mine status. Reuse of the existing convention message (`spec.md:120`) is compliant, but a status-specific message (e.g. `"El estado indicado no es válido."`) would be clearer UX. Optional polish, not a criterion failure.

### SUGGESTION-3: Schedule/closure-collision admin warning is an open follow-up (per Task 4)

- The Availability Manage view was NOT touched by this change (diff touches only the 4 new booking/admin-booking views), so the flag-don't-block warning text was correctly deferred per the task's own rule ("record as follow-up with file name"). Follow-up: add a warning in the schedule/closure Manage view when an edit collides with existing `Active` bookings, instead of silently leaving the collision.

## Drift / scope check

- No drift from proposal/spec/design: entity fields, constraints (filtered unique index `Status = 0`, both secondary indexes, both FKs `Restrict`, `decimal(18,2)`), enum values (`Active = 0, Cancelled = 1`), exception→status mapping, filter AND-semantics, ordering (`Date`, `StartTime`) all match. `POST /Bookings` accepts both `""` and `"Create"` shapes; success is MVC redirect (explicitly allowed by spec: "or MVC redirect").
- No invented requirements: `GET /Bookings/Create` form route and `Detail.cshtml` are MVC necessities for the specified flows, not scope creep. No new roles; `AppRoles.Administrator` reused in both controllers; `Program.cs` identity setup untouched (`RequireConfirmedAccount = true` intact, line 24); only addition is `AddScoped<BookingService>()` (line 31).
- No hardcoded price/duration in `src/` (grep proof above). Test assertion of rendered `"40.000"` is the es-AR `PriceDisplay` formatting — consistent, not a hardcode.
- Spanish copy verbatim: all 8 spec-table messages present exactly (`BookingService.cs:9-15`, `BookingExceptions.cs:11`); cancel-success message via `TempData["BookingMessage"]` on redirect + idempotent-repeat body — consistent with existing MVC patterns.
- Out-of-scope respected: no payments, no recurring/waitlist/guest booking, no notifications, no role changes.

## Artifacts

- This report: `openspec/changes/franco-reservas/verify-report.md`
- Memory: Engram topic `sdd/franco-reservas/verify-report` (project `rueda-la-bocha`)
