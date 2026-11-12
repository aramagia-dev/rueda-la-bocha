using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RuedaLaBocha.Web.Controllers;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Tests;

// Pago TOTAL con Padi: la reserva NO existe hasta cobrarse. El hold bloquea el
// turno 15 min; solo Succeeded con mismo enlace + importe + moneda crea el
// Booking con precio histórico. Vencido/fallido/cancelado libera sin reserva.
[Collection("Court integration")]
public sealed class BookingHoldTests
{
    private sealed class FakePadi : IPadiClient
    {
        public PadiLink Link { get; set; } = new("link-1", "https://padi.test/checkout/link-1", "https://padi.test/qr/link-1", "Active");
        public List<PadiReceivedPayment> Received { get; } = new();
        public Dictionary<string, PadiReceivedPayment> ById { get; } = new();
        public int? LastTtlMinutes { get; private set; }

        public Task<PadiLink> CreateLinkAsync(decimal amountArs, string title, CancellationToken ct = default, int? ttlMinutes = null)
        {
            LastTtlMinutes = ttlMinutes;
            return Task.FromResult(Link);
        }

        public Task<IReadOnlyList<PadiReceivedPayment>> ListReceivedPaymentsAsync(int limit = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PadiReceivedPayment>>(Received);

        public Task<PadiReceivedPayment?> GetPaymentAsync(string paymentId, CancellationToken ct = default)
            => Task.FromResult(ById.TryGetValue(paymentId, out var found) ? found : null);
    }

    private sealed class HoldTestApp : TestApp
    {
        public readonly FakePadi Fake = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            var fake = Fake;
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPadiClient>();
                services.AddSingleton<IPadiClient>(fake);
            });
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"id":"l1","checkoutUrl":"https://padi.test/c","qrCodeUrl":null,"status":"Active"}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task Payload_sin_description()
    {
        var handler = new CaptureHandler();
        var client = new PadiClient(new HttpClient(handler), Options.Create(new PadiOptions()));
        var link = await client.CreateLinkAsync(40_000m, "Reserva #1");
        Assert.Equal("l1", link.Id);
        Assert.NotNull(handler.Body);
        using var doc = JsonDocument.Parse(handler.Body);
        Assert.False(doc.RootElement.TryGetProperty("description", out _));
        Assert.Equal(40_000m, doc.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("ARS", doc.RootElement.GetProperty("currency").GetString());
        Assert.Equal("SingleUse", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(30, doc.RootElement.GetProperty("ttlMinutes").GetInt32());
    }

    [Fact]
    public async Task Dueno_crea_hold_bloquea_turno_y_muestra_enlace()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold court", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-owner@example.test");

        var created = await CourtFlowTests.Post(owner, "/Bookings/Mine",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings/Holds");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var holdId = Convert.ToInt32(app.Scalar("SELECT Id FROM BookingHolds"));
        Assert.StartsWith("/Bookings/Holds/", created.Headers.Location!.ToString());

        // Sin reserva aún: solo el hold.
        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(BookingPolicy.PriceArs, Convert.ToDecimal(app.Scalar("SELECT AmountArs FROM BookingHolds")));
        Assert.Equal(0L, app.Scalar("SELECT Status FROM BookingHolds"));
        // TTL del enlace alineado con la vigencia del hold (15 min), no con el
        // defecto de 30 min del flujo de seña.
        Assert.Equal(BookingHold.HoldMinutes, app.Fake.LastTtlMinutes);

        // El turno queda bloqueado en disponibilidad; el resto sigue libre.
        var resultados = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Reservas/Resultados?fecha={date:yyyy-MM-dd}"));
        Assert.DoesNotContain("09:00", resultados);
        Assert.Contains("10:00", resultados);

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/Holds/{holdId}"));
        Assert.Contains("https://padi.test/checkout/link-1", detail);
        Assert.Contains("https://padi.test/qr/link-1", detail);
        Assert.Contains("Comprobar estado", detail);
        Assert.Contains("Pendiente de pago", detail);
        Assert.Contains("15 minutos", detail);
    }

    [Fact]
    public async Task Segundo_hold_mismo_turno_da_conflicto()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold doble", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-doble@example.test");

        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(owner, "/Bookings/Mine",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings/Holds")).StatusCode);
        var again = await CourtFlowTests.Post(owner, "/Bookings/Mine",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings/Holds");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM BookingHolds"));
    }

    // El índice único filtrado (CourtId, Date, StartTime) donde Status=Pending
    // bloquea a nivel DB el doble hold concurrente que el chequeo previo no ve.
    [Fact]
    public async Task Doble_hold_pending_mismo_slot_falla_a_nivel_db()
    {
        using var app = new TestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold unico", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        var now = DateTime.UtcNow;
        static BookingHold Pending(int court, DateOnly d, string user, DateTime t) => new()
        {
            CourtId = court,
            UserId = user,
            Date = d,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            AmountArs = BookingPolicy.PriceArs,
            Currency = "ARS",
            Status = BookingHoldStatus.Pending,
            ExpiresAtUtc = t.AddMinutes(BookingHold.HoldMinutes),
            CreatedAtUtc = t,
            UpdatedAtUtc = t,
        };

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.BookingHolds.Add(Pending(courtId, date, "u1", now));
            await db.SaveChangesAsync();
            db.BookingHolds.Add(Pending(courtId, date, "u2", now));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Un hold liberado no bloquea uno pendiente nuevo en el mismo slot.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var first = await db.BookingHolds.SingleAsync();
            first.Status = BookingHoldStatus.Released;
            await db.SaveChangesAsync();
            db.BookingHolds.Add(Pending(courtId, date, "u2", now));
            await db.SaveChangesAsync();
        }
        Assert.Equal(2L, app.Scalar("SELECT COUNT(*) FROM BookingHolds"));
    }

    [Fact]
    public async Task Hold_vencido_libera_sin_reserva()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold vence", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-vence@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "hold-vence@example.test");
        ExpireHold(app, holdId);

        var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());
        Assert.Equal("Released", doc.RootElement.GetProperty("status").GetString());

        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(2L, app.Scalar("SELECT Status FROM BookingHolds"));
        var resultados = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Reservas/Resultados?fecha={date:yyyy-MM-dd}"));
        Assert.Contains("09:00", resultados);
    }

    [Fact]
    public async Task Succeeded_con_mismo_enlace_importe_y_moneda_crea_reserva()
    {
        using var app = new HoldTestApp();
        app.Fake.Received.Add(new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded"));
        app.Fake.ById["pay-ok"] = new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded");
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold ok", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-ok@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "hold-ok@example.test");

        var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("approved").GetBoolean());
        var bookingId = doc.RootElement.GetProperty("bookingId").GetInt32();

        // Precio histórico heredado del hold, reserva activa.
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(BookingPolicy.PriceArs, Convert.ToDecimal(app.Scalar("SELECT PriceArs FROM Bookings")));
        Assert.Equal(0L, app.Scalar("SELECT Status FROM Bookings"));
        Assert.Equal(bookingId, Convert.ToInt32(app.Scalar("SELECT BookingId FROM BookingHolds")));
        Assert.Equal(1L, app.Scalar("SELECT Status FROM BookingHolds"));

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/Holds/{holdId}"));
        Assert.Contains("Pagado", detail);
        var bookingDetail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("Detalle de la reserva", bookingDetail);
    }

    [Fact]
    public async Task Doble_Status_no_duplica_reserva()
    {
        using var app = new HoldTestApp();
        app.Fake.Received.Add(new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded"));
        app.Fake.ById["pay-ok"] = new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded");
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold redoble", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-redoble@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "hold-redoble@example.test");

        static async Task<int> Check(HttpClient owner, int holdId)
        {
            var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            Assert.True(doc.RootElement.GetProperty("approved").GetBoolean());
            return doc.RootElement.GetProperty("bookingId").GetInt32();
        }

        var first = await Check(owner, holdId);
        var second = await Check(owner, holdId);
        Assert.Equal(first, second);
        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(1L, app.Scalar("SELECT Status FROM BookingHolds"));
    }

    [Fact]
    public async Task Hold_vencido_con_pago_valido_no_crea_reserva_pero_deja_evidencia()
    {
        using var app = new HoldTestApp();
        app.Fake.Received.Add(new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded"));
        app.Fake.ById["pay-ok"] = new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded");
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold tarde", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-tarde@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "hold-tarde@example.test");
        ExpireHold(app, holdId);

        // Pago válido pero fuera de vigencia: sin confirmación automática,
        // queda el id de pago como evidencia para conciliación manual.
        var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());
        Assert.Equal("Released", doc.RootElement.GetProperty("status").GetString());

        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal("pay-ok", app.Text("SELECT PadiPaymentId FROM BookingHolds"));

        // El slot queda libre para otros.
        var resultados = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Reservas/Resultados?fecha={date:yyyy-MM-dd}"));
        Assert.Contains("09:00", resultados);
    }

    [Fact]
    public async Task Hold_liberado_concilia_evidencia_en_reintento_tardio()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold concilia", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-concilia@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "hold-concilia@example.test");
        ExpireHold(app, holdId);

        // Primer Status sin pago en Padi: libera sin evidencia.
        var first = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2L, app.Scalar("SELECT Status FROM BookingHolds"));

        // El pago aparece después en Padi: el reintento deja evidencia,
        // sin crear reserva fantasma.
        app.Fake.Received.Add(new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded"));
        app.Fake.ById["pay-ok"] = new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded");
        var retry = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var doc = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());

        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(2L, app.Scalar("SELECT Status FROM BookingHolds"));
        Assert.Equal("pay-ok", app.Text("SELECT PadiPaymentId FROM BookingHolds"));
    }

    [Fact]
    public async Task Detalle_verifica_estado_al_abrir()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold autocheck", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-autocheck@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "hold-autocheck@example.test");

        // La reapertura del detalle dispara la verificación sola (cubre el
        // caso de cerrar la página tras pagar).
        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/Holds/{holdId}"));
        Assert.Contains("Auto-check al abrir", detail);
    }

    [Theory]
    [InlineData(1000.0, "ARS")]
    [InlineData(40000.0, "USD")]
    public async Task Importe_o_moneda_distintos_no_crean(double amountValue, string currency)
    {
        var amount = Convert.ToDecimal(amountValue);
        using var app = new HoldTestApp();
        app.Fake.Received.Add(new("pay-mal", "link-1", amount, currency, "Succeeded"));
        app.Fake.ById["pay-mal"] = new("pay-mal", "link-1", amount, currency, "Succeeded");
        var courtId = await CreateCourtWithMondaySchedule(app, $"Hold mal {amount} {currency}", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        var email = $"hold-mal-{amount}-{currency}@example.test";
        using var owner = await Login(app, email);
        var holdId = await CreateHold(app, owner, courtId, date, email);

        var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());

        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(0L, app.Scalar("SELECT Status FROM BookingHolds"));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task Fallido_o_cancelado_libera_sin_reserva(string terminal)
    {
        using var app = new HoldTestApp();
        app.Fake.Received.Add(new("pay-fin", "link-1", BookingPolicy.PriceArs, "ARS", terminal));
        app.Fake.ById["pay-fin"] = new("pay-fin", "link-1", BookingPolicy.PriceArs, "ARS", terminal);
        var courtId = await CreateCourtWithMondaySchedule(app, $"Hold {terminal}", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        var email = $"hold-{terminal.ToLower()}@example.test";
        using var owner = await Login(app, email);
        var holdId = await CreateHold(app, owner, courtId, date, email);

        var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());
        Assert.Equal("Released", doc.RootElement.GetProperty("status").GetString());

        Assert.Equal(0L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        var resultados = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Reservas/Resultados?fecha={date:yyyy-MM-dd}"));
        Assert.Contains("09:00", resultados);
    }

    [Fact]
    public async Task Ajeno_no_puede_ver_ni_consultar_admin_si()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold ajeno", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-dueno@example.test");
        using var other = await Login(app, "hold-otro@example.test");
        using var admin = await app.Login(admin: true);
        var holdId = await CreateHold(app, owner, courtId, date, "hold-dueno@example.test");

        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/Bookings/Holds/{holdId}")).StatusCode);
        var statusResponse = await CourtFlowTests.Post(other, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.Forbidden, statusResponse.StatusCode);
        Assert.Contains("propias reservas", await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await CourtFlowTests.Post(other, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Link")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Bookings/Holds/{holdId}")).StatusCode);
        Assert.Equal(0L, app.Scalar("SELECT Status FROM BookingHolds"));
    }

    // H1: un hold vencido-aún-Pending no bloquea un hold nuevo en el mismo slot.
    [Fact]
    public async Task Crear_hold_sobre_slot_con_hold_vencido_pending_funciona()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold stale", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-stale@example.test");
        var first = await CreateHold(app, owner, courtId, date, "hold-stale@example.test");
        ExpireHold(app, first);

        // Disponibilidad ya lo muestra libre aunque siga Pending en la DB.
        var resultados = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Reservas/Resultados?fecha={date:yyyy-MM-dd}"));
        Assert.Contains("09:00", resultados);

        var again = await CourtFlowTests.Post(owner, "/Bookings/Mine",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings/Holds");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);

        Assert.Equal(2L, app.Scalar("SELECT COUNT(*) FROM BookingHolds"));
        Assert.Equal(2L, app.Scalar($"SELECT Status FROM BookingHolds WHERE Id = {first}"));
        Assert.Equal(0L, app.Scalar($"SELECT Status FROM BookingHolds WHERE Id <> {first}"));
    }

    // H2: solo la violación única libera; otro error de persistencia propaga.
    [Fact]
    public void IsUniqueViolation_solo_detecta_violacion_unica()
    {
        static DbUpdateException DbError(string message) => new(message, new Exception(message));
        Assert.True(BookingHoldsController.IsUniqueViolation(
            DbError("SQLite Error 19: 'UNIQUE constraint failed: BookingHolds.CourtId, BookingHolds.Date'")));
        Assert.False(BookingHoldsController.IsUniqueViolation(
            DbError("SQLite Error 19: 'NOT NULL constraint failed: Bookings.UserId'")));
        Assert.False(BookingHoldsController.IsUniqueViolation(
            DbError("SQLite Error 19: 'FOREIGN KEY constraint failed'")));
        Assert.False(BookingHoldsController.IsUniqueViolation(new DbUpdateException("boom")));
    }

    // H2/H3: el turno ocupado por OTRO sigue liberando con evidencia (no reclama).
    [Fact]
    public async Task Turno_ocupado_por_otro_libera_con_evidencia()
    {
        using var app = new HoldTestApp();
        app.Fake.Received.Add(new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded"));
        app.Fake.ById["pay-ok"] = new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded");
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold ocupado", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-ocupado@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "hold-ocupado@example.test");

        using var other = await Login(app, "hold-ocupado-otro@example.test");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var otherId = (await users.FindByEmailAsync("hold-ocupado-otro@example.test"))!.Id;
            db.Bookings.Add(new Booking
            {
                CourtId = courtId,
                UserId = otherId,
                Date = date,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                PriceArs = BookingPolicy.PriceArs,
                Status = BookingStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("approved").GetBoolean());
        Assert.Equal("Released", doc.RootElement.GetProperty("status").GetString());

        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal("pay-ok", app.Text("SELECT PadiPaymentId FROM BookingHolds"));
    }

    // H3: la ventana Booking-huérfano se reclama en vez de liberar.
    [Fact]
    public async Task Status_con_reserva_huerfana_propia_reclama_en_vez_de_liberar()
    {
        using var app = new HoldTestApp();
        app.Fake.Received.Add(new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded"));
        app.Fake.ById["pay-ok"] = new("pay-ok", "link-1", BookingPolicy.PriceArs, "ARS", "Succeeded");
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold huerfana", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-huerfana@example.test");
        var holdId = await CreateHold(app, owner, courtId, date, "hold-huerfana@example.test");

        // Un intento previo guardó el Booking pero no la transición del hold.
        int orphanId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var userId = (await users.FindByEmailAsync("hold-huerfana@example.test"))!.Id;
            var orphan = new Booking
            {
                CourtId = courtId,
                UserId = userId,
                Date = date,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                PriceArs = BookingPolicy.PriceArs,
                Status = BookingStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Bookings.Add(orphan);
            await db.SaveChangesAsync();
            orphanId = orphan.Id;
        }

        var status = await CourtFlowTests.Post(owner, "/Bookings/Mine", new(), $"/Bookings/Holds/{holdId}/Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var doc = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("approved").GetBoolean());
        Assert.Equal(orphanId, doc.RootElement.GetProperty("bookingId").GetInt32());

        Assert.Equal(1L, app.Scalar("SELECT COUNT(*) FROM Bookings"));
        Assert.Equal(1L, app.Scalar($"SELECT Status FROM BookingHolds WHERE Id = {holdId}"));
        Assert.Equal(orphanId, Convert.ToInt32(app.Scalar($"SELECT BookingId FROM BookingHolds WHERE Id = {holdId}")));
    }

    // H4: el detalle muestra el precio histórico, no el vigente.
    [Fact]
    public async Task Detalle_muestra_precio_historico()
    {
        using var app = new HoldTestApp();
        var courtId = await CreateCourtWithMondaySchedule(app, "Hold historico", new TimeOnly(9, 0), new TimeOnly(12, 0));
        var date = Next(DayOfWeek.Monday);
        using var owner = await Login(app, "hold-historico@example.test");
        int bookingId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var userId = (await users.FindByEmailAsync("hold-historico@example.test"))!.Id;
            var booking = new Booking
            {
                CourtId = courtId,
                UserId = userId,
                Date = date,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0),
                PriceArs = 40000m,
                Status = BookingStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var detail = WebUtility.HtmlDecode(await owner.GetStringAsync($"/Bookings/{bookingId}"));
        Assert.Contains("40.000", detail);
    }

    private static async Task<int> CreateHold(HoldTestApp app, HttpClient owner, int courtId, DateOnly date, string email)
    {
        Assert.Equal(HttpStatusCode.Redirect, (await CourtFlowTests.Post(owner, "/Bookings/Mine",
            new() { ["courtId"] = courtId.ToString(), ["date"] = date.ToString("yyyy-MM-dd"), ["startTime"] = "09:00" },
            "/Bookings/Holds")).StatusCode);
        string userId;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            userId = (await users.FindByEmailAsync(email))!.Id;
        }
        return Convert.ToInt32(app.Scalar(
            $"SELECT Id FROM BookingHolds WHERE CourtId = {courtId} AND Date = '{date:yyyy-MM-dd}' AND UserId = '{userId}'"));
    }

    private static void ExpireHold(HoldTestApp app, int holdId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hold = db.BookingHolds.Single(h => h.Id == holdId);
        hold.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        db.SaveChanges();
    }

    private static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }

    private static async Task<int> CreateCourtWithMondaySchedule(TestApp app, string name, TimeOnly start, TimeOnly end)
    {
        using var scope = app.Services.CreateScope();
        var courts = scope.ServiceProvider.GetRequiredService<CourtService>();
        await courts.CreateAsync(new CourtForm { Name = name, Description = "Test court", IsActive = true });
        var courtId = (await courts.ListAsync(includeInactive: true)).Single(c => c.Name == name).Id;
        await scope.ServiceProvider.GetRequiredService<CourtScheduleService>()
            .CreateScheduleAsync(courtId, DayOfWeek.Monday, start, end);
        return courtId;
    }

    private static async Task<HttpClient> Login(TestApp app, string email)
    {
        var client = app.Client();
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, TestApp.Password)).Succeeded);
        }
        var response = await CourtFlowTests.Post(client, "/Identity/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
}
