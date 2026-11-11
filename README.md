# Rueda la Bocha — guía rama `ramiro/FixFlujo`

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

## Notas QA

- Suite: **94/95**; único rojo conocido **E1** (literal `no hay pagos implementados` preservado, `tests/RuedaLaBocha.Tests/E1AgendaTests.cs`).
- **Sin pagos**: no hay integración Padi/Mercado Pago en esta rama.
