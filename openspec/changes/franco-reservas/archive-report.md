# Archive report: franco-reservas (slot-safe court reservations)

- Date: 2026-11-07
- Change: `franco-reservas`
- Branch: `feature/franco-reservas`
- Status at close: **Implemented and verified. Ready for push + single PR. No PR opened (per user instruction: PR at the very end).**
- Delivery strategy: single PR with maintainer-approved `size:exception` (session budget 2000 lines respected; standard 400-line budget exceeded by design).

## Final state

5 work-unit commits on `feature/franco-reservas` (HEAD `74d0245`):

1. `f412784` — Booking entity, mapping, AddBookings migration and snapshot test
2. `50985cf` — BookingService with availability subtraction and conflict mapping
3. `9d382aa` — Owner booking routes, views and Spanish copy
4. `66acfe7` — Admin booking list, filters and auth
5. `74d0245` — Concurrency, idempotency and acceptance sweep for 14 criteria

Position: 10 ahead of `origin/feature/franco-reservas` (5 prior sync commits + 5 new work-unit commits). Not pushed. No PR opened.

## What was built

- `Booking` entity (`Active = 0`, `Cancelled = 1`), FKs `CourtId`/`UserId` with `Restrict`, filtered unique index `(CourtId, Date, StartTime) WHERE Status = 0`, secondary indexes `(UserId, Date, Status)` and `(Date, Status)`, `PriceArs decimal(18,2)` snapshotting `BookingPolicy.PriceArs`.
- `BookingService` (create / mine / admin-list / lookup / cancel) with validation order per spec except the justified duplicate-first swap (see WARNING-1), `DbUpdateException` to `BookingConflictException` mapping, and `CourtAvailabilityService` subtraction of `Active` bookings via one extra query.
- Owner routes (`POST /Bookings`, `GET /Bookings/Mine`, `GET /Bookings/{id}`, `POST /Bookings/{id}/Cancel`) and admin routes (`GET /Admin/Bookings` with AND-combined filters, `POST /Admin/Bookings/{id}/Cancel`), MVC views + redirects, Spanish copy verbatim per spec table.
- Auth reused: `[Authorize]` for owner, `[Authorize(Roles = AppRoles.Administrator)]` for admin; Identity `RequireConfirmedAccount = true` untouched; no new roles; no `*.db` committed.

## Verification evidence (from verify-report)

- `dotnet test --nologo` → 61/61 green (1m 15s). 14/14 acceptance criteria PASS with named tests.
- `dotnet test --filter FullyQualifiedName~BookingConcurrency` → 1/1 passed, 3 consecutive runs (5s / 5s / 6s): 8 parallel creates leave exactly one `Active` row, losers get the exact Spanish conflict message, never 500.
- Diff surface: 23 files, 1625 insertions, 6 deletions — within the 2000-line session budget.
- 0 CRITICAL. 2 WARNING (justified, keep): (1) validation order swap 4↔5 required for 409; (2) 302 for 401/403 per cookie auth. 3 SUGGESTIONS non-blocking (see follow-ups).
- Spanish copy verbatim; `BookingPolicy` reused (grep `40_000|40000` over `src/` hits only `Models/BookingPolicy.cs:7`).

## Final decisions

1. Single PR with 5 work-unit commits; `size:exception` approved under the 2000-line review budget (tasks.md workload forecast).
2. Cancel keeps history (`Cancelled` status, idempotent re-cancel) and frees the slot for rebooking.
3. Duplicate prevention is two-layer (app check + filtered unique index); duplicate check runs before availability-membership check — deliberate and required.
4. Schedule/closure edits flag rather than block (collision warning deferred as follow-up; Manage view untouched in this change).
5. Out of scope held: no payments (Choni owns Mercado Pago / seña), no recurring/waitlist/guest booking, no new roles, no notifications.

## Follow-ups (post-merge, non-blocking)

1. **Spec amend — validation order.** Edit `spec.md:76-81` to swap steps 4↔5 (duplicate check before slot-validity check) with a one-line rationale: availability now subtracts bookings, so literal order would yield 400 instead of 409 on duplicates.
2. **Admin-detail HTTP test.** Add one assertion in `BookingAdminFlowTests`: admin `GET /Bookings/{id}` of another user's row → 200. Currently covered only via `User.IsInRole` wiring (code-read).
3. **Status message polish.** `Mine?status=Bogus` / `Admin?status=Bogus` return generic `"Selecciona una fecha válida."` 400. Consider a status-specific message (e.g. `"El estado indicado no es válido."`).
4. **Collision warning view.** Add a warning in the schedule/closure Manage view when an edit collides with existing `Active` bookings ("Esta edición solapa N reserva(s) activa(s); las reservas se conservan."). Bookings stay valid; new bookings follow the new grid.

## Working tree notes (for the PR session)

- Untracked planning artifacts to include or decide on: `openspec/changes/franco-reservas/` (proposal, spec, design, tasks, verify-report, this archive report) plus `openspec/.gitkeep`.
- Untracked `.atl/` (`skill-registry.md`, `.skill-registry.cache.json`) — skill-registry cache of unknown provenance; verify before PR (include only if intended, otherwise gitignore/remove).
- `*.db` clean: `git status --porcelain -- '*.db'` empty at verify time.

## Next after archive (delivery)

Push + single PR with evidence appendix (per user: PR at very end):

```powershell
git push -u origin feature/franco-reservas
gh pr create --base develop --head feature/franco-reservas --title "feat(bookings): slot-safe court reservations" --body-file <evidence-appendix>
```

Evidence appendix must include: 5-commit list, `dotnet test` 61/61 result, concurrency 3× result, 14/14 acceptance map with test names, validation-order note, `*.db`/`BookingPolicy` grep proof.

## Risks

- Branch is 10 ahead of remote and unpushed; push before any further work to avoid divergence.
- Single PR is ~1625 insertions; reviewer should read commit-by-commit (work-unit order above).
- Follow-up #1 (spec amend) should land before anyone re-implements validation order literally.
