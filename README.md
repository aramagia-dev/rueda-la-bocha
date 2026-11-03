# Rueda la bocha

Foundation for a sports court booking application built with ASP.NET Core MVC (.NET 10), Razor views, Entity Framework Core, SQLite, Bootstrap and ASP.NET Core Identity.

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

## Current scope

- MVC starter with Bootstrap and a branded home page.
- Identity starter UI and initial database migration.
- SQLite database stored locally as `src/RuedaLaBocha.Web/app.db`; database files are not versioned.
- The template requires confirmed accounts. Email delivery, production authentication configuration, and application roles are not implemented. Do not treat the starter as production-ready.
- No business modules or Mercado Pago integration yet. Never put payment credentials in source control; use development user secrets when integration begins.

## Responsibilities

| Contributor | Module |
| --- | --- |
| Luciano | Courts, features and prices |
| Ramiro | Schedules, availability and blocked slots |
| Franco | Users, roles and reservations |
| Choni | Mercado Pago and payment states |

Controllers and views handle presentation; business rules will live in services; `Data/ApplicationDbContext.cs` handles persistence. The layer boundaries and business model will be agreed before implementing modules.

## Verification scope

This initial scaffold has no automated business tests. Build and HTTP smoke checks validate the foundation; reservation and payment behavior is outside this stage.
