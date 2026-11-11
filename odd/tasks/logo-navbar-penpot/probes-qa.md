# Probes servidor logo navbar (para QA)

- Pre-reinicio: PID 8036 (`RuedaLaBocha.Web.exe` repo, `--urls http://127.0.0.1:5097`), padre 6652 (`dotnet.exe run --project .\src\RuedaLaBocha.Web --no-launch-profile --urls http://127.0.0.1:5097`). `GET /` 302, `GET /Reservas` 200.
- Detención: SOLO 8036 + 6652 (path/cmdline verificados). Puerto 5097 sin LISTENING tras parar.
- Build Debug: 0 adv / 0 err (Release también 0/0).
- Rearranque: `$env:ASPNETCORE_ENVIRONMENT='Production'` + `$env:ConnectionStrings__DefaultConnection='Data Source=$env:TEMP\opencode\rlb-iso\c3.db;Cache=Shared'` + `dotnet run --project .\src\RuedaLaBocha.Web --no-launch-profile --urls http://127.0.0.1:5097` (background, mismo comando exacto).
- Post-reinicio: PID **19004** (padre 7896, mismo cmdline), `http://127.0.0.1:5097` vivo. `GET /` ok, `GET /Reservas` ok (header logo `loading=eager` + footer `loading=lazy`, `href="/"`, wordmark + descriptores, sin emoji), `GET /images/logo-rueda-la-bocha.svg` ok (círculo `#D9EF74` 44x44).
- DB: `Temp/opencode/rlb-iso/c3.db` existente, sin reset/migración/bootstrap. Producción intacta.
- Suite: `dotnet test -c Release --no-build` **94/95** (4 NavbarLogoTests verdes). Rojo preservado: `E1_hero_y_nav_con_aviso_de_pagos_sin_fraude` (literal `no hay pagos implementados`, user edit, no se toca).
- Pendiente QA humano: navegar `http://127.0.0.1:5097/` y `/Reservas`, screenshots Chrome 1440 + mobile 390 vs exports originales, contraste, login conservando selección, Mis reservas. Aceptación humana PENDIENTE.

## SUPERSEDEDO por reversión 494 (2026-11-11)

Los probes de arriba corresponden a la versión del círculo vacío, revertida por no ser
el logo (falta silueta raster, bytes inaccesibles — ver `bloqueo-raster.md`).
Estado final: PID **16220** (padre 14132, mismo comando/env/DB `Temp/opencode/rlb-iso/c3.db`,
Production), `GET /` 302 + `/Reservas` 200 sirviendo `⚽` original en header y footer.
Screenshots del estado revertido: `nav-1440-reverted.png` + `nav-390-reverted.png`.
Suite final **90/91** (build 0/0; rojo E1 preservado).
