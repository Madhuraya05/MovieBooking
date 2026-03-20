using CinemaBooking.Data;
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
        private const decimal ConvenienceFee = 30m;
        private const int HoldMinutes = 10;

        public BookingController(AppDbContext context,UserManager<AppUser> userManager,CloudinaryService cloudinary,ILogger<BookingController> logger)
        {
            _context = context;
            _userManager =userManager;
            _cloudinary = cloudinary;
            this.logger = logger;
        }

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
                .Where(bs => bs.BookingId == booking.BookingId)
                .ToListAsync();

            foreach (var bs in bookingSeats)
                bs.Status = "Confirmed";

            var tickets = bookingSeats.Select(bs => new Ticket
            {
                BookingId = booking.BookingId,
                BookingSeatId = bs.BookingSeatId,
                TicketCode = GenerateTicketCode(),
                QRCodeData = $"CB|{GenerateTicketCode()}|{booking.ShowId}|{bs.SeatId}",
                IssuedAt = DateTime.UtcNow
            }).ToList();

            _context.Tickets.AddRange(tickets);
            await _context.SaveChangesAsync();  
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
