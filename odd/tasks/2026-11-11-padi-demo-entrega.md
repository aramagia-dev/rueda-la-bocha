# Entrega demo Padi sandbox en ramiro/Padi — 2026-11-11

Demo universitaria de seña con Padi en la rama ramiro/Padi: enlace SingleUse en ARS con QR y botón Comprobar estado por polling. Clave solo por configuración local, sin secretos en repo. Sin llamadas reales a sandbox desde subagentes; prueba con clave queda en manos humanas.

## Requisitos aprobados

- REQ-1 Demo publicada en ramiro/Padi — estado: cumple, evidencia: commit 14ebd29 sobre 1cfc1f1 con modelo migracion cliente endpoints vista tests y README
- REQ-2 Autorizacion por dueno con 403 ajeno — estado: cumple, evidencia: Create y Status validan dueno via GetForViewerAsync y convierten BookingForbiddenException en 403
- REQ-3 Importe y moneda fijados en servidor — estado: cumple, evidencia: Create usa booking.PriceArs y ARS fijo, Status solo aprueba Succeeded con mismo enlace mas importe mas moneda
- REQ-4 UI con enlace QR y comprobacion sin auto-confirmar — estado: cumple, evidencia: Detail muestra boton pagar enlace QR y Comprobar estado con polling 5s sin mutar booking
- REQ-5 Sin secretos en repo — estado: cumple, evidencia: clave solo Padi ApiKey por env o user-secrets README sin valores escaneo sin secretos
- REQ-6 Review PASS y QA APTO sin cobros reales — estado: cumple, evidencia: review PASS Ship sesion re-review y QA APTO exploratorio sesion qa con probes GET sin POST

## Archivos

| ruta | root | tipo | porque | verificacion |
|---|---|---|---|---|
| ruta: src/RuedaLaBocha.Web/Models/PadiPayment.cs | root: repo rama Padi | tipo: creado | porque: entidad de pago demo con estados | verificacion: lectura enum y campos con indice unico |
| ruta: src/RuedaLaBocha.Web/Services/PadiClient.cs | root: repo rama Padi | tipo: creado | porque: cliente sandbox poll-only con clave por config | verificacion: lectura sin log de clave y X-Api-Key condicional |
| ruta: src/RuedaLaBocha.Web/Controllers/PadiPaymentsController.cs | root: repo rama Padi | tipo: creado | porque: crear y estado con dueno e importe servidor | verificacion: lectura 403 importe ARS y Succeeded validado |
| ruta: src/RuedaLaBocha.Web/Views/Bookings/Detail.cshtml | root: repo rama Padi | tipo: editado | porque: boton pagar enlace QR y comprobar estado | verificacion: lectura polling 5s sin auto-confirmar |
| ruta: tests/RuedaLaBocha.Tests/PadiDemoTests.cs | root: repo rama Padi | tipo: creado | porque: 6 casos de auth importe y estados | verificacion: lectura 403 pendiente y Succeeded |
| ruta: odd/tasks/2026-11-11-padi-demo.md | root: repo | tipo: creado | porque: expediente del ticket sin clave | verificacion: lectura appends de implementacion |
| ruta: odd/tasks/2026-11-11-padi-demo-manifest.json | root: repo | tipo: creado | porque: requisitos aprobados aparte | verificacion: JSON REQ-1 a REQ-6 leido por gate |
| ruta: odd/tasks/2026-11-11-padi-demo-entrega.md | root: repo | tipo: creado | porque: informe standalone validado | verificacion: gate PASO contra manifest |

## Comandos

- comando: git status y git diff --stat y git diff --check en ramiro/Padi | resultado: rama Padi al dia solo untracked preexistentes diff vacio | limite: solo lectura sin mutar ramas ni DB
- comando: dotnet build -c Release reportado por implementer | resultado: 0 avisos y 0 errores | limite: implementer en Release por bloqueo de bin Debug
- comando: dotnet test reportado por implementer | resultado: 100 de 101 con unico rojo E1 preexistente de literal sin pagos | limite: QA no re-ejecuto suite por restriccion de shell
- comando: Playwright GET a http://127.0.0.1:5097/Reservas y /Bookings/1 por QA | resultado: Reservas 200 con calendario y 0 errores consola, detalle anonimo redirige a login | limite: solo GET sin crear pagos sin login

## Review y QA

- review: PASS Ship en ses_e332d4983ffevWHilEjC11HS7r turno revision, veredicto: PASS habilita QA automatico sin Must fix
- qa: APTO exploratorio en ses_e332ba3ebffevbMtbTODHjaBsP turno qa, veredicto: APTO para prueba humana con clave sandbox sin defectos bloqueantes

## Desvios

- desvios: subagentes sin clave en entorno por lo que no hubo llamadas reales a sandbox; verificado con falso mas contrato documental. Should no bloqueantes de review (primer pago del enlace, AmountArs stale, admin bypasea dueno, carrera en indice unico) quedan para endurecer fuera de demo.

## Pendientes

- pendientes: prueba humana con clave sandbox en env, crear reserva como user@gmail.com, pagar con otra cuenta Padi, conciliar con Comprobar estado; decidir Should de review; E1 seguira rojo hasta actualizar literal.

## Riesgos

- riesgos: Padi sin idempotencia ni webhooks con listado maximo 100, ante timeout la fila queda pendiente y se concilia manual; GET Status escribe estado del pago; clave real solo en entorno humano nunca en repo.

## Aceptacion

- aceptacion: PENDIENTE. Propuesta: cargar Padi ApiKey sandbox en env, levantar en Development con DB aislada y bootstrap dev, crear reserva, pagar demo, comprobar pendiente y aprobado. Sin cierre automatico.
