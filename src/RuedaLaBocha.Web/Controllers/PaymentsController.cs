using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;
using RuedaLaBocha.Web.Services;

namespace RuedaLaBocha.Web.Controllers;

// Deposit ("sena") payments through Padi (https://padi-api.mintlify.site/).
// Padi has NO webhooks, so confirmation is POLL-ONLY: Create saves a Pending
// row first and redirects to the link's checkoutUrl; Callback and
// RefreshStatus reconcile by polling the API (link + received-payments +
// confirm-by-payment-id). The GET by payment id is the source of truth.
public class PaymentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPaymentGateway _padi;
    private readonly PadiOptions _options;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        ApplicationDbContext db,
        IPaymentGateway padi,
        IOptions<PadiOptions> options,
        ILogger<PaymentsController> logger)
    {
        _db = db;
        _padi = padi;
        _options = options.Value;
        _logger = logger;
    }

    [Authorize]
    [HttpGet]
    public IActionResult Checkout() => View();

    // Starts the Padi flow: persists the Pending row FIRST (the order<->link
    // mapping lives in our db), then creates a SingleUse link and returns the
    // click-through handoff view (Result via Callback). The Result tab stays
    // open and auto-polls every 5s; the buyer opens Padi via an explicit
    // target="_blank" anchor click (popup-safe, no auto window.open).
    // NEVER auto-retry the POST on failure — Padi has no
    // idempotency key, so a retry mints a SECOND link. On timeout the row is
    // marked InProcess and reconciled from the Cobros screen instead.
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string reservaId, decimal amount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reservaId) || amount <= 0)
        {
            TempData["Error"] = "Invalid reservation or deposit amount.";
            return RedirectToAction(nameof(Checkout));
        }

        // Minimum deposit ("sena"): ARS 10,000. Rejected BEFORE any DB row
        // or Padi POST — legacy TEST-1500 rows predate this rule and stay.
        const decimal MinimumAmount = 10000m;
        if (amount < MinimumAmount)
        {
            TempData["Error"] = $"The minimum deposit is ARS {MinimumAmount:0}.";
            return RedirectToAction(nameof(Checkout));
        }

        var payment = new Payment
        {
            ReservaId = reservaId.Trim(),
            Amount = Math.Round(amount, 2),
            Status = PaymentStatus.Pending
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        PaymentLink link;
        try
        {
            link = await _padi.CreateLinkAsync(new PaymentLinkRequest(
                payment.Amount,
                $"Deposit for reservation #{payment.ReservaId}",
                Description: $"Deposit (sena) for reservation #{payment.ReservaId}",
                Type: "SingleUse",
                TtlMinutes: _options.TtlMinutes), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Ambiguous outcome: the link may exist remotely. Mark InProcess
            // and reconcile by polling — never re-POST automatically.
            _logger.LogWarning(ex, "Padi link creation timed out for payment {PaymentId}; marked InProcess", payment.Id);
            payment.Status = PaymentStatus.InProcess;
            await _db.SaveChangesAsync(ct);
            TempData["Error"] = "The payment request timed out. Check its status before trying again.";
            return RedirectToAction(nameof(Callback), new { id = payment.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Padi payment link for payment {PaymentId}", payment.Id);
            TempData["Error"] = "Could not start the checkout. Please try again.";
            return RedirectToAction(nameof(Callback), new { id = payment.Id });
        }

        payment.PaymentLinkId = link.Id;
        payment.CheckoutUrl = link.CheckoutUrl;
        payment.QrCodeUrl = link.QrCodeUrl;
        await _db.SaveChangesAsync(ct);
        // New-tab handoff: stay in THIS tab (Result auto-polls every 5s) and
        // let the buyer open Padi via an explicit target="_blank" click.
        // No same-tab Redirect(checkoutUrl) — Padi has no return URL, so the
        // same-tab redirect stranded the buyer with no way back.
        return RedirectToAction(nameof(Callback), new { id = payment.Id });
    }

    // Buyer landing after paying (or polling the state of a Pending row).
    // Display only — same poll logic as RefreshStatus.
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Callback(int id, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null)
            return NotFound();

        await ReconcileAsync(payment, ct);
        return View("Result", payment);
    }

    // "Ya pague" button: re-polls Padi and updates the row once per payment id.
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshStatus(int id, CancellationToken ct)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null)
            return NotFound();

        await ReconcileAsync(payment, ct);
        return View("Result", payment);
    }

    // Single poll pass: updates link display state, then looks for a received
    // payment on our link (confirmed by GET payment id) and applies it once.
    private async Task ReconcileAsync(Payment payment, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payment.PaymentLinkId))
            return;

        ReceivedPayment? confirmed = null;
        try
        {
            confirmed = await _padi.FindPaymentsByLinkAsync(payment.PaymentLinkId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Padi poll failed for payment {PaymentId}", payment.Id);
            return;
        }

        if (confirmed is null)
            return;

        // Update once per payment id: ignore re-polls of an already applied id.
        if (payment.PadiPaymentId == confirmed.Id)
            return;

        payment.PadiPaymentId = confirmed.Id;
        payment.RawJson = confirmed.RawJson;
        payment.Status = MapStatus(confirmed.Status);
        if (payment.Status == PaymentStatus.Approved && payment.Amount != confirmed.Amount)
            _logger.LogWarning(
                "Payment {PaymentId} amount {Local} differs from Padi amount {Remote}",
                payment.Id, payment.Amount, confirmed.Amount);

        // TODO(Franco): when the Reservation entity exists, transition
        // Reserva.Estado to Confirmada here on Approved.
        await _db.SaveChangesAsync(ct);
    }

    // ONLY Succeeded counts as paid. Pending/Processing stay open so the
    // buyer can keep polling; anything else is terminal.
    private static PaymentStatus MapStatus(string padiStatus) => padiStatus switch
    {
        "Succeeded" => PaymentStatus.Approved,
        "Failed" => PaymentStatus.Rejected,
        "Cancelled" => PaymentStatus.Cancelled,
        "Pending" or "Processing" => PaymentStatus.InProcess,
        _ => PaymentStatus.Pending,
    };
}
