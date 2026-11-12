# Rueda la Bocha — rama `ramiro/Padi` (extiende `ramiro/FixFlujo`)

Guía mínima en español para levantar y probar el flujo de reservas en desarrollo.

## Requisitos

- .NET 10 SDK.

```powershell
dotnet restore
dotnet build
```

## Arranque

Servidor de desarrollo en `http://127.0.0.1:5097`:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Data Source=$env:TEMP\opencode\rlb-iso\c3.db;Cache=Shared"
dotnet run --project src/RuedaLaBocha.Web --no-launch-profile --urls http://127.0.0.1:5097
```

Publicar:

```powershell
dotnet publish src/RuedaLaBocha.Web -c Release
```

La app usa la DB aislada `$env:TEMP\opencode\rlb-iso\c3.db` (nada fuera de esa carpeta).

## Cuentas dev (solo desarrollo)

| Rol | Email | Password |
| --- | --- | --- |
| Administrator | `admin@gmail.com` | `admin` |
| Usuario | `user@gmail.com` | `user` |

Se crean (idempotente) solo con opt-in exacto: `Development` + DB exacta autorizada + flag:

```powershell
$env:BootstrapDev__ExpectedDatabase = "$env:TEMP\opencode\rlb-iso\c3.db"
$env:BootstrapDev__AllowedRoot = "$env:TEMP\opencode\rlb-iso"
dotnet run --project src/RuedaLaBocha.Web --no-launch-profile --urls http://127.0.0.1:5097 -- --bootstrap-dev-accounts
```

Sin ambas variables no crea nada (`Dev accounts blocked`). Nunca usar en producción.

## Precio y pago

- Precio por turno: **ARS 40.000** (`BookingPolicy.PriceArs`), fútbol 5 de 60 min.
- Pago **total previo**: desde **Confirmar reserva** se crea un hold de **15 min** + enlace Padi `SingleUse` en ARS con importe fijado por el servidor. Solo `Succeeded` con mismo enlace + importe + moneda crea la reserva `Active`. Vencido/fallido/cancelado libera el turno sin reserva. La reserva NO existe hasta cobrarse. Sin webhooks ni reembolsos.

## Panel admin

- `/Admin/Dashboard` (rol `Administrator`): recaudación día/mes/año, ocupación y reservas del día.
- `/Courts/Manage`, `/Bookings`: gestión de canchas y listado admin.

## Padi sandbox (sin pagos reales)

Demo universitaria, sin claves en el repo. Configurar la clave sandbox sin commitearla:

```powershell
dotnet user-secrets set "Padi:ApiKey" "<tu-clave-sandbox>" --project src/RuedaLaBocha.Web
# o por variable de entorno:
$env:Padi__ApiKey = "<tu-clave-sandbox>"
```

Opcional: `Padi:BaseUrl` (defecto `https://padi.runasp.net`), `Padi:TtlMinutes` (defecto 30).
Probar como `user@gmail.com`: elegir turno en `/Reservas`, pagar con **Pagar total con Padi (demo)**, abrir el checkout y volver a **Comprobar estado** hasta crear la reserva. Referencia: `https://padi-api.mintlify.site/`. Límites: sin idempotencia (no reintentar crear el enlace) y el listado de pagos no pagina (máx. 100).

---
*Este README en español reemplaza al README inglés base solo en estas ramas (`ramiro/FixFlujo`, `ramiro/Padi`); conciliar al fusionar a `develop`.*
