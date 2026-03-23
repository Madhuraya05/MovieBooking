using CinemaBooking.Data;
using CinemaBooking.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieBooking.Infrastructure.Services;
using MovieBooking.Models;
using MovieBooking.Web.Models;
using System.Diagnostics;

namespace MovieBooking.Web.Controllers
{
    public class BookingController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly CloudinaryService _cloudinary;
        private readonly ILogger<BookingController> logger;
        private readonly EmailService emailService;
        private const decimal ConvenienceFee = 30m;
        private const int HoldMinutes = 10;

        public BookingController(AppDbContext context,UserManager<AppUser> userManager,CloudinaryService cloudinary,ILogger<BookingController> logger,EmailService emailService)
        {
            _context = context;
            _userManager =userManager;
            _cloudinary = cloudinary;
            this.logger = logger;
            this.emailService = emailService;
        }

        /// <summary>
        /// Displays the seat selection view for a specified show, allowing users to choose available seats for booking.
        /// </summary>
        /// <remarks>Only scheduled shows that have not yet started can be selected. If the show is
        /// unavailable or has already started, the user is redirected and notified. The seat map displays available and
        /// booked seats for the selected show.</remarks>
        /// <param name="showId">The unique identifier of the show for which seats are to be selected. Must correspond to a scheduled and
        /// available show.</param>
        /// <returns>An IActionResult that renders the seat selection view if the show is available; otherwise, redirects to the
        /// show listing page with an error message.</returns>
        [Authorize]
        public async Task<IActionResult> SelectSeats(int showId)
        {
            var show = await _context.Shows
                .Include(s => s.Movie)
                .Include(s => s.Screen).ThenInclude(sc => sc.Theatre)
                .FirstOrDefaultAsync(s => s.ShowId == showId && s.Status == "Scheduled");

            if (show == null)
            {
                TempData["Error"] = "This show is no longer availablle";
                return RedirectToAction("Index","Show");
            }

            var showDateTime = show.ShowDate.Date + show.StartTime;
            if (showDateTime < DateTime.Now)
            {
                TempData["Error"] = "This show is no longer available";
                return RedirectToAction("Index", "Show");
            }

            var seats = await _context.Seats
                .Where(s => s.ScreenId == show.ScreenId && s.IsActive)
                .OrderBy(s => s.RowLabel).ThenBy(s => s.SeatNumber)
                .ToListAsync();
            logger.LogInformation($"ShowId={showId} ScreenId={show.ScreenId} SeatsFound={seats.Count}");

            var takenSeatIds = await _context.BookingSeats
                .Where(bs => bs.ShowId == showId && (bs.Status == "Held" || bs.Status == "Confirmed"))
                .Select(bs => bs.SeatId)
                .ToListAsync();

            var seatsByRow = seats
                .GroupBy(s => s.RowLabel)
                .OrderBy(g => g.Key)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(s => new SeatMapItem
                    {
                        SeatId = s.SeatId,
                        RowLabel = s.RowLabel,
                        SeatNumber = s.SeatNumber,
                        Category = s.Category,
                        BasePrice = s.BasePrice,
                        FinalPrice = Math.Round(s.BasePrice * show.PriceMultiplier, 0),
                        Status = takenSeatIds.Contains(s.SeatId) ? "Booked" : "Available"
                    }).ToList()
                );

            var model = new SeatMapViewModel
            {
                ShowId = showId,
                MovieTitle = show.Movie.Title,
                PosterUrl = _cloudinary.GetResizedUrl(show.Movie.PosterUrl, 150, 225),
                ShowDate = show.ShowDate,
                StartTime = show.StartTime,
                TheatreName = show.Screen.Theatre.Name,
                ScreenName = show.Screen.ScreenName,
                ScreenType = show.Screen.ScreenType,
                Language = show.Language,
                SeatsByRow = seatsByRow,
                PriceMultiplier = show.PriceMultiplier,
            };

            return View(model);
        }

        /// <summary>
        /// IT hold the seat with tencapacity
        /// </summary>
        /// <param name="request"></param>
        /// <returns>redirect to selectseat on error otherwise on summary</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HoldSeats(HoldSeatsRequest request)
        {
            var seatIds = request.GetSeatIds();

            if (!seatIds.Any())
            {
                TempData["Error"] = "Please select at least one seat";
                return RedirectToAction(nameof(SelectSeats), new { showId = request.ShowId });
            }

            if (seatIds.Count > 10)
            {
                TempData["Error"] = "You can book a maximum of 10 seats at a time";
                return RedirectToAction(nameof(SelectSeats), new { showId = request.ShowId });
            }

            var userId = _userManager.GetUserId(User)!;

            var show = await _context.Shows
                .Include(s => s.Movie)
                .Include(s => s.Screen).ThenInclude(sc => sc.Theatre)
                .FirstOrDefaultAsync(s => s.ShowId == request.ShowId && s.Status == "Scheduled");

            if (show == null)
            {
                TempData["Error"] = "This show is no longer available.";
                return RedirectToAction("Index", "Show");
            }

            var seats = await _context.Seats
                .Where(s => seatIds.Contains(s.SeatId) &&
                s.ScreenId == show.ScreenId &&
                s.IsActive)
                .ToListAsync();

            if (seats.Count != seatIds.Count)
            {
                TempData["Error"] = "One or more selected seats are invalid";
                return RedirectToAction(nameof(SelectSeats),new {showId = request.ShowId});
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var bookingSeats = seats.Select(s => new BookingSeat
                {
                    SeatId = s.SeatId,
                    ShowId = request.ShowId,
                    SeatPrice = Math.Round(s.BasePrice * show.PriceMultiplier, 2),
                    Status = "Held",
                    HeldAt = DateTime.UtcNow,
                }).ToList();

                var subTotal = bookingSeats.Sum(bs => bs.SeatPrice);
                var totalAmount = subTotal + ConvenienceFee;

                var reference = GenerateBookingReference();

                var booking = new Booking
                {
                    UserId = userId,
                    ShowId = request.ShowId,
                    BookingReference = reference,
                    TotalAmount = totalAmount,
                    ConvenienceFee = ConvenienceFee,
                    Status = "Pending",
                    BookedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(HoldMinutes)
                };

                _context.Bookings.Add(booking);

                await _context.SaveChangesAsync();

                foreach (var bs in bookingSeats)
                    bs.BookingId = booking.BookingId;

                _context.BookingSeats.AddRange(bookingSeats);

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return RedirectToAction(nameof(Summary), new {id = booking.BookingId});
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();

                TempData["Error"] =
                    "One or more seats were just taken by another user. " +
                    "Please reselect your seats";

                return RedirectToAction(nameof(SelectSeats), new {showId = request.ShowId});
            }
        }

        /// <summary>
        /// it gives the summary of the booking
        /// </summary>
        /// <param name="id"></param>
        /// <returns>An IActionResult that renders the booking summary view if the booking is found</returns>
        [Authorize]
        public async Task<IActionResult> Summary(int id)
        {
            var userId = _userManager.GetUserId(User);

            var booking = await _context.Bookings
                .Include(b => b.Show).ThenInclude(s => s.Movie)
                .Include(b => b.Show).ThenInclude(s => s.Screen).ThenInclude(sc => sc.Theatre)
                .Include(b => b.BookingSeats).ThenInclude(bs => bs.Seat)
                .FirstOrDefaultAsync(b => b.BookingId == id && b.UserId == userId);

            if (booking == null) return NotFound();

            if (booking.ExpiresAt.ToUniversalTime() < DateTime.UtcNow.AddSeconds(-30) && booking.Status == "Pending")
            {
                await ReleaseExpiredBooking(booking);
                TempData["Error"] = "Your booking timed out. Please try again";
                return RedirectToAction(nameof(SelectSeats), new { showId = booking.ShowId });
            }

            if (booking.Status == "Confirmed")
                return RedirectToAction(nameof(Confirmation), new { id });

            var model = new BookingSummaryViewModel
            {
                BookingId = booking.BookingId,
                BookingReference = booking.BookingReference,
                ShowId = booking.ShowId,
                MovieTitle = booking.Show.Movie.Title,
                PosterUrl = _cloudinary.GetResizedUrl(booking.Show.Movie.PosterUrl, 150, 225),
                ShowDate = booking.Show.ShowDate,
                StartTime = booking.Show.StartTime,
                TheatreName = booking.Show.Screen.Theatre.Name,
                ScreenName = booking.Show.Screen.ScreenName,
                City = booking.Show.Screen.Theatre.City,
                Language = booking.Show.Language,
                Seats = booking.BookingSeats.Select(bs => new SeatSummaryItem
                {
                    SeatId = bs.SeatId,
                    SeatLabel = $"{bs.Seat.RowLabel}{bs.Seat.SeatNumber}",
                    Category = bs.Seat.Category,
                    Price = bs.SeatPrice
                }).OrderBy(s => s.SeatLabel).ToList(),
                SubTotal = booking.TotalAmount - booking.ConvenienceFee,
                ConvenienceFee = booking.ConvenienceFee,
                TotalAmount = booking.TotalAmount,
                ExpiresAt = booking.ExpiresAt
            };

            return View(model);
        }

        /// <summary>
        /// it confirms the bookingid and check for the booking expiry time
        /// </summary>
        /// <param name="bookingId"></param>
        /// <returns>An IActionResult that redirects to checkout payment </returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ProceedToPayment(int bookingId)
        {
            var userId = _userManager.GetUserId(User);
            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId);

            if (booking == null) return NotFound();

            if (booking.ExpiresAt< DateTime.UtcNow)
            {
                await ReleaseExpiredBooking(booking);
                TempData["Error"] = "Your booking timed out .";
                return RedirectToAction(nameof(SelectSeats),new {showId = booking.ShowId});
            }

            return RedirectToAction("Checkout","Payment",new {bookingId});
        }

        /// <summary>
        /// Displays the booking confirmation page for the specified booking identifier, if the booking belongs to the
        /// current user.
        /// </summary>
        /// <remarks>This action requires the user to be authenticated. Only bookings associated with the
        /// current user can be accessed.</remarks>
        /// <param name="id">The unique identifier of the booking to display. Must correspond to a booking owned by the current user.</param>
        /// <returns>An IActionResult that renders the booking confirmation view if the booking is found; otherwise, a NotFound
        /// result.</returns>
        [Authorize]
        public async Task<IActionResult> Confirmation(int id)
        {
            var userId = _userManager.GetUserId(User);

            var booking = await _context.Bookings
                .Include(b => b.Show).ThenInclude(s => s.Movie)
                .Include(b => b.Show).ThenInclude(s => s.Screen).ThenInclude(sc => sc.Theatre)
                .Include(b => b.Tickets)
                .FirstOrDefaultAsync(b => b.BookingId == id && b.UserId == userId);

            if (booking == null) return NotFound();

            var model = new BookingSummaryViewModel
            {
                BookingId = booking.BookingId,
                BookingReference = booking.BookingReference,
                ShowId = booking.ShowId,
                MovieTitle = booking.Show.Movie.Title,
                PosterUrl = _cloudinary.GetResizedUrl(booking.Show.Movie.PosterUrl, 300, 450),
                ShowDate = booking.Show.ShowDate,
                StartTime = booking.Show.StartTime,
                TheatreName = booking.Show.Screen.Theatre.Name,
                ScreenName = booking.Show.Screen.ScreenName,
                City = booking.Show.Screen.Theatre.City,
                Language = booking.Show.Language,
                Seats = booking.BookingSeats.Select(bs => new SeatSummaryItem
                {
                    SeatId = bs.SeatId,
                    SeatLabel = $"{bs.Seat.RowLabel}{bs.Seat.SeatNumber}",
                    Category = bs.Seat.Category,
                    Price = bs.SeatPrice
                }).OrderBy(s => s.SeatLabel).ToList(),
                SubTotal = booking.TotalAmount - booking.ConvenienceFee,
                ConvenienceFee = booking.ConvenienceFee,
                TotalAmount = booking.TotalAmount,
                ExpiresAt = booking.ExpiresAt
            };

            return View(model);
        }

        /// <summary>
        /// Displays a list of bookings made by the currently authenticated user.
        /// </summary>
        /// <remarks>This action requires the user to be authenticated. Only bookings associated with the
        /// current user's account are displayed.</remarks>
        /// <returns>An <see cref="IActionResult"/> that renders the booking history view for the current user. The view model
        /// contains a list of the user's past bookings, ordered by booking date.</returns>
        [Authorize]
        public async Task<IActionResult> MyBookings()
        {
            var userId = _userManager.GetUserId(User);

            var bookings = await _context.Bookings
                                    .Where(b => b.UserId == userId)
                                    .Include(b => b.Show).ThenInclude(s => s.Movie)
                                    .Include(b => b.Show).ThenInclude(s => s.Screen).ThenInclude(sc => sc.Theatre)
                                    .Include(b => b.BookingSeats)
                                    .OrderByDescending(b => b.BookedAt)
                                    .ToListAsync();

            var history = bookings.Select(b => new BookingHistoryItem
            {
                BookingId = b.BookingId,
                BookingReference = b.BookingReference,
                MovieTitle = b.Show.Movie.Title,
                PosterUrl = _cloudinary.GetResizedUrl(b.Show.Movie.PosterUrl, 80, 120),
                ShowDate = b.Show.ShowDate,
                StartTime = b.Show.StartTime,
                TheatreName = b.Show.Screen.Theatre.Name,
                City = b.Show.Screen.Theatre.City,
                SeatCount = b.BookingSeats.Count,
                TotalAmount = b.TotalAmount,
                Status = b.Status,
                BookedAt = b.BookedAt
            }).ToList();

            return View(history);
        }

        /// <summary>
        /// Cancels a booking with the specified booking ID for the currently authenticated user.
        /// </summary>
        /// <remarks>Confirmed bookings cannot be cancelled through this action. Users must contact
        /// support for refunds on confirmed bookings. Only bookings belonging to the current user can be
        /// cancelled.</remarks>
        /// <param name="bookingId">The unique identifier of the booking to cancel. Must correspond to a booking owned by the current user.</param>
        /// <returns>A redirect to the MyBookings view if the cancellation is successful or not permitted; otherwise, a NotFound
        /// result if the booking does not exist.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Cancel(int bookingId)
        {
            var userId = _userManager.GetUserId(User)!;

            var booking = await _context.Bookings
                .Include(b =>b.BookingSeats)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId);

            if (booking == null) return NotFound();

            if (booking.Status == "Confirmed")
            {
                TempData["Error"] =
                    "Confirmed bookings cannot be cancelled here. " +
                    "Contact support for refunds";
                return RedirectToAction(nameof(MyBookings));
            }

            await ReleaseExpiredBooking(booking);
            TempData["Success"] = "Booking cancelled successfully";
            return RedirectToAction(nameof(MyBookings));
        }

        private async Task ConfirmBooking(Booking booking)
        {
                booking.Status = "Confirmed";

                var bookingSeats = await _context.BookingSeats
                    .Include(bs => bs.Seat)
                    .Where(bs => bs.BookingId == booking.BookingId)
                    .ToListAsync();

                foreach (var bs in bookingSeats)
                    bs.Status = "Confirmed";

                // Generate tickets — one per seat
                var tickets = bookingSeats.Select(bs => new Ticket
                {
                    BookingId = booking.BookingId,
                    BookingSeatId = bs.BookingSeatId,
                    TicketCode = GenerateTicketCode(),
                    QRCodeData = $"CB|TKT-{Guid.NewGuid().ToString("N")[..8].ToUpper()}|{booking.ShowId}|{bs.SeatId}",
                    IssuedAt = DateTime.UtcNow
                }).ToList();

                _context.Tickets.AddRange(tickets);
                await _context.SaveChangesAsync();

                // ── SEND CONFIRMATION EMAIL ───────────────────────────────────────────
                // Load full show data for email (might not be included in booking yet)
                var show = await _context.Shows
                    .Include(s => s.Movie)
                    .Include(s => s.Screen).ThenInclude(sc => sc.Theatre)
                    .FirstOrDefaultAsync(s => s.ShowId == booking.ShowId);

                // Load user email
                var user = await _userManager.FindByIdAsync(booking.UserId);

                if (user != null && show != null)
                {
                    // Map each ticket + bookingSeat to TicketEmailItem
                    var ticketItems = tickets.Select(t =>
                    {
                        var bs = bookingSeats.First(bs => bs.BookingSeatId == t.BookingSeatId);
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

                    // Fire and forget — don't await, don't block the user's redirect
                    // If email fails, it's logged but booking is still confirmed
                    _ = emailService.SendBookingConfirmationAsync(
                        user.Email!,
                        user.FullName,
                        emailData);
                }
            }
        private async Task ReleaseExpiredBooking(Booking booking)
        {
            booking.Status = "Cancelled";

            var held = await _context.BookingSeats
                .Where(bs => bs.BookingId == booking.BookingId && bs.Status == "Held")
                .ToListAsync();

            foreach (var bs in held)
                bs.Status = "Released";

            await _context.SaveChangesAsync();
        }

        private static string GenerateBookingReference()
        {
            var year = DateTime.Now.Year;
            var random = Guid.NewGuid().ToString("N")[..6].ToUpper();
            return $"CB-{year}-{random}";
        }

        // Generate unique ticket code: TKT-A1B2C3D4
        private static string GenerateTicketCode()
        {
            return "TKT-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        }
        public IActionResult Index()
        {
            return View();
        }
    }
}
