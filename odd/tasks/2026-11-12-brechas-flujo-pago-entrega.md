# Entrega brechas flujo pago B1 B2 B3 en ramiro/Padi — 2026-11-12

Tres bugs funcionales reportados con capturas: reserva pagada que re-ofrece pago (B1), Mis reservas sin estados ni filtros (B2), Confirmar crea reserva sin pagar (B3). Causa común: doble modelo (Bookings directo + PadiPayments vieja conviviendo con Holds de pago previo). Fix: creación directa deshabilitada, Detail por hold origen, Mine con filtro.

## Requisitos aprobados

- REQ-1 B3 sin reserva sin pago — estado: cumple, evidencia: POST Bookings y Create devuelven 400 sin crear nada, vista Create sin boton Confirmar solo Pagar total con Padi, PadiPayments Create rechaza Active Succeeded o Consumed
- REQ-2 B1 pagada no re-ofrece pago — estado: cumple, evidencia: Detail /Bookings/10 muestra Pagado con ID 2ad68b6f y comprobante sin boton ni QR, 2 recargas sin reaparicion, canceladas /3 /7 sin seccion de pago
- REQ-3 B2 estados y filtros — estado: cumple, evidencia: nav Todas Activas Canceladas con activo marcado, insignias Activa Cancelada, status Active 6 filas Cancelled 5 filas Bogus 400, movil 360 sin overflow
- REQ-4 Sin regresiones — estado: cumple, evidencia: build Release 0 errores, suite 122 de 122 en 37 s, review PASS, QA APTO, consola 0 errores en flujo, hold 7 sin pagar no crea reserva
- REQ-5 Sin secretos ni toques fuera de rama — estado: cumple, evidencia: sin secretos en codigo logs o commit, develop main intactos, DB aislada preservada, sin pagos reales

## Archivos

| ruta | root | tipo | porque | verificacion |
|---|---|---|---|---|
| ruta: src/RuedaLaBocha.Web/Controllers/BookingsController.cs | root: repo rama Padi | tipo: editado | porque: POST Create devuelve 400 sin crear, Detail usa ViewBag Hold Consumed por BookingId, Mine expone status | verificacion: lectura POST 400 Detail Hold Mine filtro mas review PASS |
| ruta: src/RuedaLaBocha.Web/Controllers/PadiPaymentsController.cs | root: repo rama Padi | tipo: editado | porque: Create rechaza Active Succeeded o Consumed y elimina creacion vieja de enlace | verificacion: lectura guards y redirect con ya tiene pago total mas test |
| ruta: src/RuedaLaBocha.Web/Views/Bookings/Create.cshtml | root: repo rama Padi | tipo: editado | porque: sin boton Confirmar solo Pagar total con Padi mas nota de pago total | verificacion: lectura botones solo Pagar total mas QA sin Confirmar |
| ruta: src/RuedaLaBocha.Web/Views/Bookings/Detail.cshtml | root: repo rama Padi | tipo: editado | porque: seccion Pagado por hold sin boton ni QR, legacy Active con nota, Cancelled sin pago | verificacion: lectura secciones mas QA recargas |
| ruta: src/RuedaLaBocha.Web/Views/Bookings/Mine.cshtml | root: repo rama Padi | tipo: editado | porque: nav de filtro Todas Activas Canceladas con activo marcado | verificacion: lectura nav mas QA filtros y movil |
| ruta: tests/RuedaLaBocha.Tests/BookingOwnerFlowTests.cs | root: repo rama Padi | tipo: editado | porque: POST directo 400 sin crear y siembra por servicio | verificacion: suite 122 de 122 |
| ruta: tests/RuedaLaBocha.Tests/PadiDemoTests.cs | root: repo rama Padi | tipo: editado | porque: rechazo sobre Active, Detail Pagado y por hold, legacy, Cancelled, filtro Mine | verificacion: PadiDemoTests 7 de 7 |
| ruta: tests/RuedaLaBocha.Tests/ReservaCalendarioTests.cs | root: repo rama Padi | tipo: editado | porque: confirmacion exige hold sin Confirmar | verificacion: suite 122 de 122 |
| ruta: tests/RuedaLaBocha.Tests/ReservasFetchTests.cs | root: repo rama Padi | tipo: editado | porque: siembra por servicio en vez de POST | verificacion: suite 122 de 122 |
| ruta: odd/tasks/2026-11-12-brechas-flujo-pago.md | root: repo | tipo: creado | porque: expediente del ticket con aprobaciones y transiciones | verificacion: lectura ticket rama aprobaciones status Pendientes |
| ruta: odd/tasks/2026-11-12-brechas-flujo-pago-manifest.json | root: repo | tipo: creado | porque: requisitos aprobados aparte | verificacion: JSON REQ-1 a REQ-5 leido por gate |
| ruta: odd/tasks/2026-11-12-brechas-flujo-pago-entrega.md | root: repo | tipo: creado | porque: informe standalone validado | verificacion: gate PASO contra manifest |

## Comandos

- comando: dotnet build -c Release ejecutado por padre | resultado: 0 errores | limite: incremental tras build de implementer
- comando: dotnet test -c Release ejecutado por padre | resultado: 122 de 122 Superado 0 con error en 37 s | limite: DB de tests propia sin tocar c3.db
- comando: Playwright QA en :5097 por QA sin pagar nada real | resultado: B3 sin Confirmar y POST 400, B1 Pagado sin reaparicion, B2 filtros 6 y 5 filas, hold 7 sin reserva | limite: sin completar pagos en Padi
- comando: curl anonimo a /Bookings/5/Padi/Status por padre | resultado: 302 a login | limite: endpoint viejo sin UI que lo invoque
- comando: Select-String de fail exception 500 en server.log por padre | resultado: solo aviso https port y query Identity, sin 500 | limite: log del proceso actual tras reinicio

## Review y QA

- review: PASS ses_e32a5b474ffeFhhTFSDcFtmOfN veredicto: PASS sin defectos reales, unico creador BookingHolds Status con validaciones, sin doble cobro, autorizacion dueno y antiforgery verificados
- qa: APTO ses_e32a35bc9ffep4hJy2cEPxTtzS veredicto: APTO B1 B2 B3 y regresion hold sin pagar, con ruido 500 en tabs ajenas no generadas por el flujo actual

## Desvios

- desvios: POST Create devuelve 400 con cuerpo de mensaje en vez de redirect para no enmascarar el rechazo; PadiPayments Create declara flujo viejo deshabilitado por comentario; reclamo de huerfana del ticket anterior intacto
- desvios: QA sin shell por restriccion, contrasto suite del padre; BookingConcurrencyTests fragil en corridas intermedias en path no tocado, verde en final

## Pendientes

- pendientes: aceptacion humana como prueba final en http://127.0.0.1:5097 con codigo nuevo; commit y push a ramiro/Padi solo con orden explicita; agregar aria-current al filtro activo de Mine
- pendientes: BookingService CreateAsync quedo sin llamante HTTP (reservado para uso futuro o admin); PadiPayments Status viejo sigue expuesto pero sin UI que lo invoque

## Riesgos

- riesgos: cambios sin commit en disco, si otro proceso pisa el arbol se pierden; reservas legacy Active anteriores al pago total muestran nota sin admitir cobro y requieren conciliacion manual si alguien pago por fuera
- riesgos: no verificado concurrencia masiva lector de pantalla ni contraste; nav de filtro usa color de boton sin aria-current

## Aceptacion

- aceptacion: PENDIENTE. Propuesta: recargar /Bookings/10 y verificar Pagado sin opcion de pagar; crear hold y verificar que solo existe Pagar total; filtrar Mis reservas; luego ordenar commit y push a ramiro/Padi. Sin cierre automatico.
