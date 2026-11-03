using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Security;
using RuedaLaBocha.Web.Services;

var bootstrapAdmin = args.Contains("--bootstrap-admin", StringComparer.Ordinal);
if (bootstrapAdmin && (args.Contains("--help", StringComparer.Ordinal) || args.Length != 1))
{
    Console.WriteLine("Usage: dotnet run --project src/RuedaLaBocha.Web --no-launch-profile -- --bootstrap-admin");
    Console.WriteLine("Set BootstrapAdmin:Email and BootstrapAdmin:Password in private configuration. Apply database migrations first.");
    Console.WriteLine("Creates a new administrator only; existing accounts are never promoted. Remove the secrets after success.");
    Environment.ExitCode = args.Length == 2 && args.Contains("--help", StringComparer.Ordinal) ? 0 : 1;
    return;
}
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--bootstrap-admin").ToArray());

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
builder.Services.AddScoped<AdminProvisioner>();

var app = builder.Build();

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
