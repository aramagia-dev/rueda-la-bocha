# Entrega logo navbar PNG original — 2026-11-11

Alcance: integrar el PNG original del usuario en el header, sin redibujar, sin tocar footer, hero ni textos. Servidor vivo http://127.0.0.1:5097 para prueba humana.

## Requisitos aprobados

- REQ-1 PNG original byte-exacto sin redibujar — estado: cumple, evidencia: copia byte-exacta Group(1).png 180x180 11043 bytes SHA256 EFF90112 igual en destino
- REQ-2 Solo header, footer inalterado — estado: cumple, evidencia: _Layout linea 17 img local y linea 41 footer conserva emoji sin hunk
- REQ-3 Hero nav y textos intactos — estado: cumple, evidencia: H1 Tu proximo partido empieza aca y copiespreservados en capturas desktop y movil
- REQ-4 Desktop y movil sin distorsion ni recorte — estado: cumple, evidencia: Playwright Chrome 1440 display 48x48 y 390 display 40x40 object-fit contain sin overflowX
- REQ-5 Asset local accesible sin externos — estado: cumple, evidencia: GET images logo 200 image/png 11043 bytes alt vacio decorativo width height 180
- REQ-6 CSS con alcance solo header — estado: cumple, evidencia: site.css 2 reglas site-header brand-mark fondo blanco contain sin tocar regla global lima
- REQ-7 Build y tests con rojo conocido preservado — estado: cumple, evidencia: build Release 0 advertencias 0 errores y test 94 de 95 con unico fallo E1 literal no hay pagos implementados sin tocar
- REQ-8 QA navegador sin errores y sin escrituras — estado: cumple, evidencia: Chrome 1440 y 390 Reservas OK consola 0 errores red PNG 200 sin reservas ni pagos ni admin

## Archivos

| ruta | root | tipo | porque | verificacion |
|---|---|---|---|---|
| ruta: src/RuedaLaBocha.Web/wwwroot/images/logo-rueda-la-bocha.png | root: repo | tipo: creado | porque: destino local del PNG original | verificacion: existe 11043 bytes 180x180 GET 200 image/png |
| ruta: src/RuedaLaBocha.Web/Views/Shared/_Layout.cshtml | root: repo | tipo: editado | porque: header usa img local y footer conserva emoji | verificacion: lectura lineas 17 y 41 y diff sin hunk footer |
| ruta: src/RuedaLaBocha.Web/wwwroot/css/site.css | root: repo | tipo: editado | porque: 2 reglas con alcance site-header brand-mark | verificacion: lectura lineas 29-30 y footer global intacto |
| ruta: tests/RuedaLaBocha.Tests/NavbarLogoTests.cs | root: repo | tipo: creado | porque: 4 tests de bytes dims header footer servido y CSS | verificacion: lectura codigo y suite 94 de 95 |
| ruta: odd/tasks/logo-navbar-penpot/entrega-logo.md | root: repo | tipo: creado | porque: informe standalone validado | verificacion: gate PASO contra manifest-logo.json |
| ruta: odd/tasks/logo-navbar-penpot/manifest-logo.json | root: repo | tipo: creado | porque: requisitos aprobados aparte | verificacion: JSON con REQ-1 a REQ-8 leido por gate |

Fuente original intocada en src/RuedaLaBocha.Web/img/Group(1).png, no se modifica ni se mueve.

## Comandos

- comando: dotnet build -c Release --nologo -v minimal | resultado: Compilacion correcta 0 advertencias 0 errores | limite: solo compilacion, sin publicar ni migrar
- comando: dotnet test -c Release --no-build --nologo | resultado: 94 superado 1 con error 95 total unico fallo E1 literal preservado | limite: sin tocar copia de usuario ni debilitar assert
- comando: GET http://127.0.0.1:5097/ y /Reservas y /images/logo-rueda-la-bocha.png | resultado: 302 y 200 y 200 image/png 11043 bytes | limite: solo lectura GET sin reservas sin pagos sin admin
- comando: Playwright Chrome 1440x900 y 390x844 en / y /Reservas | resultado: logo visible header footer emoji hero intacto consola 0 errores | limite: solo Chrome, sin Firefox sin dispositivo fisico sin lector

## Review y QA

- review: Ship PASS con 3 should-fix no bloqueantes y 3 nice-to-have en ses_e335484a7ffe6SgkLIpIM1uUNJ turno 526, veredicto: PASS habilita QA
- qa: PASS condicional header-only en ses_e3353765cffe2vAGQ88snqxtlg turno 531, veredicto: PASS sin defectos bloqueantes build dotnet pendiente a padre por restriccion de rol

## Desvios

- desvios: ninguno fuera de header-only. Fuente real 180x180 RGBA medida, no 350x295 estimado. No se recorto padding blanco para preservar bytes exactos. Sticky sobre fondo no blanco mostraria cuadrado blanco, hoy header es blanco.

## Pendientes

- pendientes: aceptacion humana con prueba en http://127.0.0.1:5097 en desktop y movil; ejecutar contraste y lector; verificar produccion; limpiar working tree con cambios previos fuera de logo y capturas QA en raiz; decidir 3 should-fix de review (assert header footer, overflow hidden circular, hash servido).

## Riesgos

- riesgos: overflow visible en marca circular sin clip explicito, solo visible en render real y hoy sin recorte observado; alt vacio decorativo pendiente de validar con lector; working tree amplio fuera de logo dificulta atribuir cambios sin commit.

## Aceptacion

- aceptacion: PENDIENTE. Prueba humana propuesta: abrir / y /Reservas en 1440 y 390, confirmar logo nitido sin recorte, footer con emoji, hero y textos intactos, sin errores visibles. Sin cierre automatico.
