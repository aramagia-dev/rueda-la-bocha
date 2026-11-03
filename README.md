# Rueda la bocha

Football-five court management built with ASP.NET Core MVC (.NET 10), Razor views, Entity Framework Core, SQLite, Bootstrap and ASP.NET Core Identity.

## Run locally

Install the .NET 10 SDK. From this directory:

```powershell
dotnet restore
dotnet tool restore
dotnet ef database update --project src/RuedaLaBocha.Web
dotnet run --project src/RuedaLaBocha.Web --launch-profile http
```

Open http://localhost:5097. In Visual Studio with .NET 10 support, open `RuedaLaBocha.slnx` and select `RuedaLaBocha.Web` as the startup project.

```powershell
dotnet build --no-restore
```

## Court management

- `/Courts` is a public catalog of active courts. All courts are football five, with 60-minute slots at ARS 40,000. These shared rules are in `Models/BookingPolicy.cs`; the price is the full slot cost, not a deposit.
- `/Courts/Manage` is restricted to the `Administrator` role. Administrators can create, edit, deactivate and reactivate courts. Deactivation hides the court from the public catalog without deleting its record.
- Names are required (maximum 100 characters); descriptions are optional (maximum 1,000 characters). All changes use protected POST forms with server-side validation and antiforgery checks.
- SQLite database stored locally as `src/RuedaLaBocha.Web/app.db`; database files are not versioned.
- Reservations, availability and Mercado Pago are not implemented. Never put payment credentials in source control.

### Luciano's interface

The home page, public court catalog and administration screens use a responsive green/cream football theme inspired by the supplied prototype. Home previews up to three real active courts in catalog order; an empty complex shows an honest empty state, not sample courts. The catalog shows all active courts. Pitch illustrations are decorative, not court photographs.

User-facing court copy and shared navigation are in Spanish. Identity's built-in account pages retain their existing content and behavior; their full localization belongs to the account module. Navigation links, mobile menu, skip link and form actions lead to existing features only. Availability search, reservations and payment controls are deliberately absent.

## Provision the first administrator

Run migrations first. Provisioning is an explicit local operation, never a public registration option. Use a **new email**: existing accounts are never promoted, confirmed or reset by this command. Repeating provisioning for the same email fails without changing the account.

In Visual Studio, right-click `RuedaLaBocha.Web` and select **Manage User Secrets**. Add the following keys to that private JSON file using your own email and a strong unique password; do not put real values in this README, appsettings or GitHub:

Identity requires at least six characters, including uppercase, lowercase, a digit and a non-alphanumeric symbol. Prefer a unique password of at least 12 characters. Provisioning deliberately reports a generic failure; check these requirements and use a new account before retrying.

```json
{
  "BootstrapAdmin:Email": "your-admin-email@example.com",
  "BootstrapAdmin:Password": "REPLACE_WITH_A_PRIVATE_STRONG_PASSWORD"
}
```

From the solution directory run:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/RuedaLaBocha.Web --no-launch-profile -- --bootstrap-admin
```

The command exits after provisioning; it does not start a web server. Exit code 0 means success; nonzero means failure (invalid/missing configuration, existing account or unavailable database). Account, role and role membership are committed in one transaction. The new locally provisioned account is marked confirmed so it can sign in. Existing users are not modified.

**Remove both bootstrap keys from User Secrets after success**, then start the application normally and sign in. Private configuration alone never creates an administrator during normal web startup. For deployment, supply those same keys through your secret manager/environment instead of User Secrets. Do not put a password in command-line arguments or share it in chat.

Public registration always creates an ordinary, non-administrator account. Identity still requires account confirmation. The template's development confirmation page may show a confirmation link because email delivery is not configured; this does not verify mailbox ownership. Configure real email delivery and review production security before deployment.

## Responsibilities

| Contributor | Module |
| --- | --- |
| Luciano | Courts, features and prices |
| Ramiro | Schedules, availability and blocked slots |
| Franco | Users, roles and reservations |
| Choni | Mercado Pago and payment states |

Controllers and views handle presentation; `Services/CourtService.cs` handles court operations; `Data/ApplicationDbContext.cs` handles persistence. Reuse `BookingPolicy` for the shared sport, duration and price in later modules, and `AppRoles.Administrator` for administrator authorization.

## Verification scope

```powershell
dotnet test
```

If a running development instance locks build outputs on Windows, leave it running and use `dotnet test --configuration Release` instead. The provisioning test uses the same build configuration as the test assembly.

Integration tests use real Identity cookies and antiforgery forms with isolated SQLite databases under the system temporary directory. They exercise public consultation, administrator CRUD, rejected requests without data changes, role overposting, provisioning, Spanish interface content, real court previews, shared account navigation and POST logout. The local application database is never used by tests. Reservation/payment behavior and production security deployment are outside this stage.
