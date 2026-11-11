using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

var bootstrapAdmin = args.Contains("--bootstrap-admin", StringComparer.Ordinal);
var bootstrapDev = args.Contains("--bootstrap-dev-accounts", StringComparer.Ordinal);
if ((bootstrapAdmin || bootstrapDev) && (args.Contains("--help", StringComparer.Ordinal) || args.Length != 1))
{
    Console.WriteLine("Usage: dotnet run --project src/RuedaLaBocha.Web --no-launch-profile -- --bootstrap-admin");
    Console.WriteLine("   or: dotnet run --project src/RuedaLaBocha.Web --no-launch-profile -- --bootstrap-dev-accounts");
    Console.WriteLine("Set BootstrapAdmin:Email and BootstrapAdmin:Password in private configuration. Apply database migrations first.");
    Console.WriteLine("Creates a new administrator only; existing accounts are never promoted. Remove the secrets after success.");
    Console.WriteLine("Dev accounts run only in Development with an isolated temp database and never log credentials.");
    Environment.ExitCode = args.Length == 2 && args.Contains("--help", StringComparer.Ordinal) ? 0 : 1;
    return;
}
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--bootstrap-admin" && a != "--bootstrap-dev-accounts").ToArray());

// Ejecuciones desde el árbol fuente (dotnet run / tests) no usan el layout de
// publish: registrar las raíces de contenido del build (wwwroot/obj/paquetes)
// igual que en Development para que MapStaticAssets resuelva bundle con ámbito,
// huellas y variantes comprimidas. Lo publicado no trae obj/ y usa su manifiesto.
if (Directory.Exists(Path.Combine(builder.Environment.ContentRootPath, "obj")))
{
    builder.WebHost.UseStaticWebAssets();
}

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<CourtService>();
builder.Services.AddScoped<CourtScheduleService>();
builder.Services.AddScoped<CourtAvailabilityService>();
builder.Services.AddScoped<ICourtAvailabilityService>(provider => provider.GetRequiredService<CourtAvailabilityService>());
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<AdminProvisioner>();

// Padi demo (sandbox): clave solo desde configuración privada
// (user-secrets "Padi:ApiKey" o variable de entorno Padi__ApiKey).
builder.Services.Configure<PadiOptions>(builder.Configuration.GetSection("Padi"));
builder.Services.AddHttpClient<IPadiClient, PadiClient>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<PadiOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
        http.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
});

var app = builder.Build();

if (bootstrapDev)
{
    using var scope = app.Services.CreateScope();
    try
    {
        var (adminCreated, userCreated) = await scope.ServiceProvider.GetRequiredService<AdminProvisioner>().EnsureDevAccountsAsync();
        if (adminCreated || userCreated)
        {
            Console.WriteLine($"Dev accounts ready. Admin created: {adminCreated}. User created: {userCreated}. Existing accounts preserved.");
            Environment.ExitCode = 0;
        }
        else
        {
            Console.WriteLine("Dev accounts blocked. Existing accounts preserved; no changes made. Check Development environment, exact authorized database and collisions.");
            Environment.ExitCode = 1;
        }
    }
    catch
    {
        Console.Error.WriteLine("Dev accounts not created. Check Development environment and isolated database.");
        Environment.ExitCode = 1;
    }
    return;
}

if (bootstrapAdmin)
{
    using var scope = app.Services.CreateScope();
    try
    {
        var created = await scope.ServiceProvider.GetRequiredService<AdminProvisioner>().CreateAsync();
        Console.WriteLine(created ? "Administrator created. Remove the bootstrap secrets before normal startup."
            : "Administrator not created. Check private configuration and use a new account. Existing accounts are never promoted.");
        Environment.ExitCode = created ? 0 : 1;
    }
    catch
    {
        Console.Error.WriteLine("Administrator not created. Check database migrations and private configuration.");
        Environment.ExitCode = 1;
    }
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
