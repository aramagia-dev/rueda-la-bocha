# Entrega panel admin dashboard en ramiro/Padi — 2026-11-12

El admin pasa de Administrar canchas a un Panel con recaudación día mes año, ocupación, calendario por día y lista de activas. QA inicial NO APTO solo por overflow móvil; fix CSS acotado + re-QA APTO.

## Requisitos aprobados

- REQ-9 Recaudación y métricas — estado: cumple, evidencia: tarjetas día ARS 1 mes ARS 3 año ARS 3 ocupación 33 por ciento 1 de 3, pagos pendientes 0, canceladas 12, desglose Cancha 1 ARS 1 y Cancha 2 ARS 0, solo Active con precio histórico en zona Argentina
- REQ-10 Calendario por día y lista de activas — estado: cumple, evidencia: Vanilla como Reservas con ?date=, día 12 sin reservas con mensaje, fecha inválida 200 con aviso de hoy, todas las activas 3 filas
- REQ-11 Admin-only y canchas intactas — estado: cumple, evidencia: user común AccessDenied y anónimo Login sin Panel en nav, Courts Manage con CRUD vivo, Admin Bookings accesible
- REQ-12 Sin regresiones ni overflow — estado: cumple, evidencia: build Release 0 errores, suite 131 de 131, review PASS dos veces, QA APTO, 360px docScroll 345 con tablas en scroll interno, Reservas desktop intacto, consola 0 errores

## Archivos

| ruta | root | tipo | porque | verificacion |
|---|---|---|---|---|
| ruta: src/RuedaLaBocha.Web/Controllers/AdminDashboardController.cs | root: repo rama Padi | tipo: creado | porque: Index con totales Active ocupación holds canceladas desglose y día | verificacion: lectura agregaciones AsNoTracking mas review PASS |
| ruta: src/RuedaLaBocha.Web/Models/DashboardViewModel.cs | root: repo rama Padi | tipo: creado | porque: 3 records sin lógica | verificacion: lectura mas review PASS |
| ruta: src/RuedaLaBocha.Web/Views/Dashboard/Index.cshtml | root: repo rama Padi | tipo: creado | porque: tarjetas calendario Vanilla listas y enlaces a canchas y reservas | verificacion: lectura mas QA desktop y móvil |
| ruta: src/RuedaLaBocha.Web/Views/Shared/_Layout.cshtml | root: repo rama Padi | tipo: editado | porque: nav admin Administrar canchas a Panel | verificacion: lectura mas QA roles |
| ruta: src/RuedaLaBocha.Web/wwwroot/css/site.css | root: repo rama Padi | tipo: editado | porque: 5 reglas de contención móvil y renombre de tarjeta | verificacion: lectura reglas mas re-QA 345 en 360 |
| ruta: tests/RuedaLaBocha.Tests/AdminDashboardTests.cs | root: repo rama Padi | tipo: creado | porque: 4 tests totales día auth fecha y tarjeta CSS | verificacion: suite 131 de 131 |
| ruta: odd/tasks/2026-11-12-admin-dashboard.md | root: repo | tipo: creado | porque: expediente del ticket con aprobaciones y push previo | verificacion: lectura ticket rama y transiciones |
| ruta: odd/tasks/2026-11-12-admin-dashboard-manifest.json | root: repo | tipo: creado | porque: requisitos aparte | verificacion: JSON REQ-9 a REQ-12 leido por gate |
| ruta: odd/tasks/2026-11-12-admin-dashboard-entrega.md | root: repo | tipo: creado | porque: informe standalone validado | verificacion: gate PASO contra manifest |

## Comandos

- comando: dotnet build -c Release ejecutado por padre | resultado: 0 errores | limite: tras detener servidor previo que bloqueaba el exe
- comando: dotnet test -c Release ejecutado por padre | resultado: 131 de 131 Superado 0 con error en 38 s | limite: DB de tests propia sin tocar c3.db
- comando: Playwright QA en :5097 por QA sin pagar nada real | resultado: NO APTO inicial por overflow 658 en 360, APTO tras fix con 345 y tablas contenidas | limite: subagente sin shell, suite del padre tomada como dada
- comando: servidor :5097 reiniciado por padre con código dashboard | resultado: 200 en Reservas y Dashboard | limite: arranque lento superó el loop de espera inicial

## Review y QA

- review: PASS ses_e326e2fa5ffeqcP71BSXw7m4z8y ses_e326415e3ffe5S3Hw0axHJSvBo veredicto: PASS dashboard y PASS fix CSS sin colaterales en Reservas Manage ni Availability
- qa: APTO ses_e32624b3bffe0ksqRCTdWun9Qc veredicto: APTO móvil 345 sin desborde y desktop sin regresión, SEÑALES ausente

## Desvios

- desvios: implementer compiló con salida temporal porque el exe estaba bloqueado; el padre reconstruyó Release y verificó 131 de 131; tarjeta SEÑALES PENDIENTES renombrada a PAGOS PENDIENTES porque ya no hay señas
- desvios: QA sin shell por restricción, contrastó suite del padre; un servidor intermedio del implementer fue detenido para compilar

## Pendientes

- pendientes: aceptación humana como prueba final en http://127.0.0.1:5097; commit y push del dashboard a ramiro/Padi solo con orden explícita (el push 5580516 no lo incluye)
- pendientes: booking 13 pagada para slot ya pasado (prueba del usuario, sin acción)

## Riesgos

- riesgos: cambios del dashboard sin commit en disco, si otro proceso pisa el árbol se pierden; DB aislada con datos de prueba del usuario
- riesgos: no verificado lector de pantalla ni contraste; vendor del calendario podría inyectar ancho fijo en el futuro

## Aceptacion

- aceptacion: PENDIENTE. Propuesta: como admin abrir Panel, cambiar de día en el calendario y revisar tarjetas; como user verificar que no hay Panel; luego ordenar commit y push a ramiro/Padi. Sin cierre automático.
