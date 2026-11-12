# Ticket 2026-11-12-admin-dashboard — Panel admin con recaudación y calendario

- Rama: `ramiro/Padi`; sin tocar `develop`/`main`.
- Aprobado en turno real: ticket + push + dashboard con agregados (§804→§805 sí, hazlo).
- Push cumplido: commit `5580516` sobre `14ebd29`, `ramiro/Padi` sincronizada con origen; sin secretos (verificado en diff staged); efímeros (`bin/obj`, `.atl/`, `.codegraph/`, pngs QA, etc.) fuera del commit.
- Alcance dashboard: tarjetas día/mes/año (suma ARS de Active, zona America/Argentina), ocupación del día, conteo pendientes (holds) y canceladas, desglose por cancha; calendario Vanilla como Inicio con `?date=` que muestra reservas del día; lista completa de activas; solo admin; nav "Administrar canchas"→"Panel", CRUD canchas vivo con enlace desde el Panel.
- Status: plan aprobado en turno real (§818 sí) → implementado → review PASS → QA APTO (tras fix overflow móvil + renombre SEÑALES→PAGOS PENDIENTES con re-review PASS y re-QA APTO).
- Números: build Release 0 errores, suite 131/131 (4 tests AdminDashboardTests), consola 0 errores desktop y 360px, docScroll 345 en 360px.
- Aceptación humana: PENDIENTE. Sin commit/push del dashboard (push 5580516 anterior no lo incluye).
