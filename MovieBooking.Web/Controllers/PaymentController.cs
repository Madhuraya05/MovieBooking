using CinemaBooking.Data;
using CinemaBooking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieBooking.Models;
using Stripe;

namespace MovieBooking.Web.Controllers
{
    public class PaymentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IConfiguration configuration;
        private readonly ILogger<PaymentController> logger;
        private readonly EmailService _emailService;

        public PaymentController(AppDbContext context, UserManager<AppUser> userManager,IConfiguration configuration,ILogger<PaymentController> logger,EmailService emailService)
        {
            this._context = context;
            this._userManager = userManager;
            this.configuration = configuration;
            this.logger = logger;
            _emailService = emailService;
        }

        [HttpPost]
        [Authorize]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateIntent(int bookingId)
        {
            var userId = _userManager.GetUserId(User)!;
            var booking = await _context.Bookings
                .Include(b => b.Show).ThenInclude(s => s.Movie)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && 
                b.UserId == userId && b.Status == "Pending"
                );

            if (booking == null)
                return Json(new { error = "Booking not found or already processed." });

            if (booking.ExpiresAt < DateTime.UtcNow)
                return Json(new { error = "Booking has expired. Please select seats again." });

            string clientSecret;

            if (!string.IsNullOrEmpty(booking.Payment?.StripePaymentIntentId))
            {
                var service = new PaymentIntentService();
                var existing = await service.GetAsync(booking.Payment.StripePaymentIntentId);
                clientSecret = existing.ClientSecret;
            }
            else
            {
                var options = new PaymentIntentCreateOptions
                {
                    Amount = (long)(booking.TotalAmount * 100),
                    Currency = "inr",

                    Metadata = new Dictionary<string, string>
                    {
                        {"booking_id",booking.BookingId.ToString() },
                        {"booking__reference",booking.BookingReference },
                        {"user_id",userId }
                    },

                    Description = $"MovieBook - {booking.Show.Movie.Title} - {booking.BookingReference}",

                    AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                    {
                        Enabled = true,
                    }

                };
                var service = new PaymentIntentService();
                var intent = await service.CreateAsync(options);
                clientSecret = intent.ClientSecret;
                var payment = new Payment
                {
                    BookingId = booking.BookingId,
                    StripePaymentIntentId = intent.Id,
                    Amount = booking.TotalAmount,
                    Currency = "inr",
                    Status = "Pending"
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();
            }

            return Json(new
            {
                clientSecret,
                publishableKey = configuration["Stripe:PublishableKey"],
                amount = booking.TotalAmount,
                bookingReference = booking.BookingReference
            });
        }

        [Authorize]
        public async Task<IActionResult> Checkout(int bookingId)
        {
            var userId = _userManager.GetUserId(User);

            var booking = await _context.Bookings
                .Include(b => b.Show).ThenInclude(s => s.Movie)
                .Include(b => b.Show).ThenInclude(s => s.Screen).ThenInclude(sc => sc.Theatre)
                .Include(b => b.BookingSeats).ThenInclude(bs => bs.Seat)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId);

            if (booking == null) return NotFound();

            if (booking.Status == "Confirmed")
                return RedirectToAction("Confirmation", "Booking", new { id = bookingId });

            if (booking.ExpiresAt < DateTime.UtcNow)
            {
                TempData["Error"] = "Your booking has expired. Please try again.";
                return RedirectToAction("SelectSeats", "Booking",
                    new { showId = booking.ShowId });
            }

            ViewBag.Booking = booking;
            ViewBag.PublishableKey = configuration["Stripe:PublishableKey"];
            return View(booking);
        }

        [Authorize]
        public async Task<IActionResult> Return(int bookingId, string? payment_intent)
        {
            if (string.IsNullOrEmpty(payment_intent))
                return RedirectToAction("Summary", "Booking", new { id = bookingId });

            var service = new PaymentIntentService();
            var intent = await service.GetAsync(payment_intent);

            return intent.Status switch
            {
                "succeeded" => RedirectToAction("WaitingConfirmation",
                new { bookingId, paymentIntentId = payment_intent }),

                "requires_action" => RedirectToAction("Checkout", new { bookingId }),

                _ => RedirectToAction("PaymentFailed", new { bookingId })
            };
        }

        [Authorize]
        public async Task<IActionResult> WaitingConfirmation(
            int bookingId,string paymentIntentId)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId);

            if (booking?.Status == "Confirmed")
                return RedirectToAction("Confirmation", "Booking", new { id = bookingId });

            ViewBag.BookingId = bookingId;
            ViewBag.PaymentIntentId = paymentIntentId;
            return View();
        }

        [Authorize]
        public async Task<IActionResult> CheckStatus(int bookingId)
        {
            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.BookingId==bookingId);

            return Json(new { confirmed = booking?.Status == "Confirmed" });
        }

        [Authorize]
        public IActionResult PaymentFailed(int bookingId)
        {
            ViewBag.BookingId = bookingId;
            return View();
        }


        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> WebHook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            
            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    configuration["Stripe:WebHookSecret"]
                    );
            }
            catch (Exception ex)
            {
                return BadRequest(new {error = ex.Message});
            }

            switch (stripeEvent.Type)
            {
                case EventTypes.PaymentIntentSucceeded:
                    await HandlePaymentSucceeded(stripeEvent);
                    break;

                case EventTypes.PaymentIntentPaymentFailed:
                    await HandlePaymentFailed(stripeEvent);
                    break;
            }

            return Ok();
        }

        private async Task HandlePaymentSucceeded(Event stripeEvent)
        {
            var intent = stripeEvent.Data.Object as PaymentIntent;
            if (intent == null) return;
            var payment = await _context.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b.BookingSeats)
                    .ThenInclude(bs => bs.Seat)
                .FirstOrDefaultAsync(p => p.StripePaymentIntentId == intent.Id);

            if (payment == null) return;

            // Idempotency check — don't process the same event twice
            if (payment.Status == "Succeeded") return;

            // Update Payment record
            payment.Status = "Succeeded";
            payment.StripeChargeId = intent.LatestChargeId;
            payment.PaymentMethod = intent.PaymentMethodTypes.FirstOrDefault();
            payment.PaidAt = DateTime.UtcNow;

            // Confirm the Booking
            var booking = payment.Booking;
            booking.Status = "Confirmed";

            // Confirm all BookingSeats
            foreach (var bs in booking.BookingSeats)
                bs.Status = "Confirmed";
            var code = "TKT-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            // Generate Tickets — one per seat
            var tickets = booking.BookingSeats.Select(bs => new Ticket
            {
                BookingId = booking.BookingId,
                BookingSeatId = bs.BookingSeatId,
                TicketCode = code,
                QRCodeData = $"CB|TKT-{Guid.NewGuid().ToString("N")[..8].ToUpper()}|{booking.ShowId}|{bs.SeatId}",
                IssuedAt = DateTime.UtcNow
            }).ToList();

            _context.Tickets.AddRange(tickets);
            await _context.SaveChangesAsync();

            // TODO Phase 8: Send confirmation email with tickets here
            var show = await _context.Shows
        .Include(s => s.Movie)
        .Include(s => s.Screen).ThenInclude(sc => sc.Theatre)
        .FirstOrDefaultAsync(s => s.ShowId == booking.ShowId);

            var user = await _userManager.FindByIdAsync(booking.UserId);

            if (user != null && show != null)
            {
                var ticketItems = tickets.Select(t =>
                {
                    var bs = booking.BookingSeats
                        .First(bs => bs.BookingSeatId == t.BookingSeatId);
                    return new TicketEmailItem
                    {
                        TicketCode = t.TicketCode,
                        SeatLabel = $"{bs.Seat.RowLabel}{bs.Seat.SeatNumber}",
                        Category = bs.Seat.Category,
                        Price = bs.SeatPrice
                    };
                }).ToList();

                var emailData = new BookingEmailData
                {
                    BookingReference = booking.BookingReference,
                    MovieTitle = show.Movie.Title,
                    ShowDate = show.ShowDate,
                    StartTime = show.StartTime,
                    TheatreName = show.Screen.Theatre.Name,
                    ScreenName = show.Screen.ScreenName,
                    City = show.Screen.Theatre.City,
                    Language = show.Language,
                    SubTotal = booking.TotalAmount - booking.ConvenienceFee,
                    ConvenienceFee = booking.ConvenienceFee,
                    TotalAmount = booking.TotalAmount,
                    Tickets = ticketItems
                };

                // Fire and forget — webhook must return 200 quickly
                // Email failure will NOT affect booking confirmation
                _ = _emailService.SendBookingConfirmationAsync(
                    user.Email!,
                    user.FullName,
                    emailData);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE: Handle failed payment
        // ─────────────────────────────────────────────────────────────────────
        private async Task HandlePaymentFailed(Event stripeEvent)
        {
            var intent = stripeEvent.Data.Object as PaymentIntent;
            if (intent == null) return;

            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.StripePaymentIntentId == intent.Id);

            if (payment == null) return;

            payment.Status = "Failed";
            await _context.SaveChangesAsync();

            // Note: BookingSeats remain "Held" — user can retry payment
            // They will be released by SeatExpiryService when ExpiresAt passes
        }
    }

}

