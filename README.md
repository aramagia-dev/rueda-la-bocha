# Rueda la Bocha — guía rama `ramiro/Padi` (extiende `ramiro/FixFlujo`)

Guía mínima en español para levantar y probar el flujo de reservas en desarrollo.

## Requisitos

- .NET 10 SDK.
- Restaurar y compilar desde la raíz del repo:

```powershell
dotnet restore
dotnet build
```

## Arranque (comando exacto)

Servidor de desarrollo en `http://127.0.0.1:5097`:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Data Source=$env:TEMP\opencode\rlb-iso\c3.db;Cache=Shared"
dotnet run --project src/RuedaLaBocha.Web --no-launch-profile --urls http://127.0.0.1:5097
```

## URLs

- `/` — inicio.
- `/Reservas` — flujo de reserva por fecha.

## Flujo Reservas

1. Elegir **fecha** en el calendario (`/Reservas?fecha=AAAA-MM-DD`).
2. Elegir **cancha** activa con huecos.
3. Elegir **hora** (slot de 60 min).
4. **Login**: si no hay sesión, pide login y **preserva la selección** para continuar.
5. Confirmar → ver en **Mis reservas**; cancelar/reactivar desde ahí.
6. **Admin**: `/Courts/Manage`, `/Bookings` (listado admin) requieren rol `Administrator`.

## Usuarios de prueba (solo desarrollo, sin secretos reales)

| Rol | Email | Password dev | Cómo obtenerlos |
| --- | --- | --- | --- |
| Administrator | `admin@gmail.com` | `admin` | Solo `Development` + DB exacta autorizada, con `dotnet run ... -- --bootstrap-dev-accounts` |
| Usuario | `user@gmail.com` | `user` | Idem anterior (se crean juntos, idempotente) |

Detalles: `src/RuedaLaBocha.Web/Security/AdminProvisioner.cs` y `tests/RuedaLaBocha.Tests/BootstrapDevAccountsTests.cs`. Nunca usar en producción; las cuentas existentes nunca se promueven ni resetean.

### Bootstrap dev (requiere opt-in exacto)

`--bootstrap-dev-accounts` solo crea cuentas si además están estas 2 variables (`AdminProvisioner.cs:89-91`, `BootstrapDevAccountsTests.cs:166-171`):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Data Source=$env:TEMP\opencode\rlb-iso\c3.db;Cache=Shared"
$env:BootstrapDev__ExpectedDatabase = "$env:TEMP\opencode\rlb-iso\c3.db"
$env:BootstrapDev__AllowedRoot = "$env:TEMP\opencode\rlb-iso"
dotnet run --project src/RuedaLaBocha.Web --no-launch-profile --urls http://127.0.0.1:5097 -- --bootstrap-dev-accounts
```

Sin ambas variables (DB exacta `$env:TEMP\opencode\rlb-iso\c3.db` bajo root `$env:TEMP\opencode\rlb-iso`, a su vez bajo `GetTempPath()`), termina con `Dev accounts blocked` (`Program.cs:62`) y no crea nada.

## Notas QA

- Suite: **100/101**; único rojo conocido **E1** (literal `no hay pagos implementados` preservado, `tests/RuedaLaBocha.Tests/E1AgendaTests.cs`).
- **Pagos**: solo demo Padi sandbox (sin dinero real); el snapshot de migraciones apunta a `20261111225033_AddPadiPayments`.

## Padi demo (sandbox, sin pagos reales)

- Estado: **implementado como demo universitaria** en esta rama; no hay claves en el repo.
- Flujo: en el detalle de una reserva propia activa, botón **Pagar con Padi (demo)** → crea un enlace `SingleUse` en ARS con el importe fijado por el servidor (`Booking.PriceArs`) → muestra enlace (nueva pestaña) + QR → botón **Comprobar estado** consulta Padi por polling (reintenta cada 5 s mientras sigue pendiente). Solo `Succeeded` con mismo enlace + importe + moneda marca aprobado; `Pending`/`Processing` queda pendiente, nunca aprobado. Sin webhooks, sin reembolso, sin auto-confirmar ni cancelar reservas.
- Configurar la clave sandbox (nunca commitearla, no va en logs ni tests):
  ```powershell
  dotnet user-secrets set "Padi:ApiKey" "<tu-clave-sandbox>" --project src/RuedaLaBocha.Web
  # o por variable de entorno:
  $env:Padi__ApiKey = "<tu-clave-sandbox>"
  ```
  Opcional: `Padi:BaseUrl` (defecto `https://padi.runasp.net`) y `Padi:TtlMinutes` (defecto 30).
- Probar con cuentas dev `admin@gmail.com` / `user@gmail.com` (ver bootstrap arriba): crear la reserva como `user@gmail.com`, abrir su detalle y pagar con otra cuenta Padi (el checkout muestra “Cobro propio” si usás la misma cuenta vendedora).
- Referencia (solo lectura, sin copiar secretos): docs del API en `https://padi-api.mintlify.site/` y rama `origin/feature/choni-pagos` (`fe951e2`, demo Padi poll-only, no fusionada; acá se corrigen sus defectos: dueño validado, importe fijado en servidor).
- Límites conocidos: Padi no deduplica (sin idempotencia): no reintentar crear el enlace; ante timeout la fila queda pendiente y se concilia con Comprobar estado. El listado de pagos recibidos no pagina (máx. 100).

---
*Este README en español reemplaza al README inglés base solo en estas ramas (`ramiro/FixFlujo`, `ramiro/Padi`); conciliar al fusionar a `develop`.*
