# Entrega B4 holds pendientes en Mis reservas en ramiro/Padi — 2026-11-12

B4 del ticket 2026-11-12-brechas-flujo-pago: salir de un hold sin pagar lo hacía irrecuperable. Fix: Mine muestra holds Pending vigentes con Retomar pago + filtro Pendientes.

## Requisitos aprobados

- REQ-6 Pendiente visible y retomable — estado: cumple, evidencia: hold 14 sin pagar visible en Mine con cancha fecha hora ARS 1 vencimiento insignia y Retomar pago a /Bookings/Holds/14 con checkout QR y comprobar
- REQ-7 Filtros y vencidos — estado: cumple, evidencia: Pending solo holds sin tabla, Todas ambas secciones, Active 2 filas y Cancelled sin holds, usuario nuevo sin holds con mensaje, vencido excluido con slot libre
- REQ-8 Sin regresiones — estado: cumple, evidencia: build Release 0 errores, suite 127 de 127, review PASS, QA APTO, B1 B2 B3 intactos, movil 360 sin overflow

## Archivos

| ruta | root | tipo | porque | verificacion |
|---|---|---|---|---|
| ruta: src/RuedaLaBocha.Web/Controllers/BookingsController.cs | root: repo rama Padi | tipo: editado | porque: Mine carga PendingHolds propios vigentes con Court y acepta status Pending | verificacion: lectura filtro UserId Pending futura mas review PASS |
| ruta: src/RuedaLaBocha.Web/Views/Bookings/Mine.cshtml | root: repo rama Padi | tipo: editado | porque: pestana Pendientes de pago y seccion Pagos pendientes con Retomar pago | verificacion: lectura seccion mas QA filtros y movil |
| ruta: tests/RuedaLaBocha.Tests/BookingPendingHoldsMineTests.cs | root: repo rama Padi | tipo: creado | porque: 5 tests propio ajeno vencido filtro y retomar | verificacion: suite 127 de 127 |
| ruta: odd/tasks/2026-11-12-brechas-flujo-pago.md | root: repo | tipo: editado | porque: expediente del ticket con B4 y dato de cancelaciones del usuario | verificacion: lectura append B4 mas CancelledAtUtc en DB |
| ruta: odd/tasks/2026-11-12-brechas-flujo-pago-b4-manifest.json | root: repo | tipo: creado | porque: requisitos B4 aparte | verificacion: JSON REQ-6 a REQ-8 leido por gate |
| ruta: odd/tasks/2026-11-12-brechas-flujo-pago-b4-entrega.md | root: repo | tipo: creado | porque: informe standalone validado | verificacion: gate PASO contra manifest |

## Comandos

- comando: dotnet build -c Release ejecutado por padre | resultado: 0 errores | limite: tras detener servidor previo que bloqueaba el exe
- comando: dotnet test -c Release ejecutado por padre | resultado: 127 de 127 Superado 0 con error en 36 s | limite: DB de tests propia sin tocar c3.db
- comando: Playwright QA en :5097 por QA sin pagar nada real | resultado: hold 14 visible y retomable, filtros verificados, B1 B2 B3 intactos | limite: holds de 15 min expiran entre corridas
- comando: consulta DB c3.db por padre | resultado: bookings 5 6 8 9 10 11 12 Cancelled 01:36 UTC por usuario, 13 y 14 Active, holds 13 14 Pending | limite: solo lectura

## Review y QA

- review: PASS ses_e328ddb73ffehWlYqY7ZeOrA46 veredicto: PASS propios mas Pending mas futura con Court, Pending sin tabla, sin mutacion en GET, antiforgery intacto
- qa: APTO ses_e328bf66cffeNB43Kr98WGzHCA veredicto: APTO caso 1 filtros regresion y movil, observacion menor sin mensaje en Todas vacia

## Desvios

- desvios: implementer compilo en Debug porque un servidor ajeno bloqueaba Release, el padre reconstruyo Release 0 errores y 127 de 127; servidor :5097 reiniciado por el padre con codigo B4
- desvios: QA sin shell por restriccion, contrasto suite del padre; seccion Pendientes oculta en Active y Cancelled por semantica de filtro

## Pendientes

- pendientes: aceptacion humana como prueba final en http://127.0.0.1:5097; commit y push a ramiro/Padi solo con orden explicita; mensaje para Todas vacia cuando solo hay seccion Pendientes
- pendientes: booking 13 pagada para slot 11/11 18:00 ya pasado (prueba del usuario, sin accion)

## Riesgos

- riesgos: cambios sin commit en disco, si otro proceso pisa el arbol se pierden; DB aislada con datos de prueba del usuario (cancelaciones y pagos $1 reales en sandbox)
- riesgos: no verificado lector de pantalla ni contraste; aria-current pendiente del ticket base

## Aceptacion

- aceptacion: PENDIENTE. Propuesta: crear hold sin pagar, verificarlo en Mis reservas con Retomar, filtrar Pendientes; luego ordenar commit y push a ramiro/Padi. Sin cierre automatico.
