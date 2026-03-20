using CinemaBooking.Data;
using MovieBooking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MovieBooking.Models;
using MovieBooking.Web.Models;
using Mysqlx.Crud;
using MovieBooking.Infrastructure.Services;
using Org.BouncyCastle.Bcpg;

namespace MovieBooking.Web.Controllers
{
    
    public class ShowController : Controller
    {
        private readonly AppDbContext _context;
        private readonly CloudinaryService _cloudinary;

        public ShowController(AppDbContext context,CloudinaryService cloudinary)
        {
            _context = context;
            this._cloudinary = cloudinary;
        }

        public async Task<IActionResult> Index(int? movieId, string? city, DateTime? date)
        {
            // Default to today if no date filter
            var filterDate = date ?? DateTime.Today;

            var query = _context.Shows
                .Where(s => s.Status == "Scheduled" && s.ShowDate >= DateTime.Today)
                .Include(s => s.Movie)
                .Include(s => s.Screen).ThenInclude(sc => sc.Theatre)
                .AsQueryable();

            if (movieId.HasValue)
                query = query.Where(s => s.MovieId == movieId);

            if (!string.IsNullOrWhiteSpace(city))
                query = query.Where(s => s.Screen.Theatre.City == city);

            if (date.HasValue)
                query = query.Where(s => s.ShowDate.Date == date.Value.Date);

            var shows = await query
                .OrderBy(s => s.ShowDate)
                .ThenBy(s => s.StartTime)
                .ToListAsync();

            // Build ShowListViewModel with seat availability counts
            var showIds = shows.Select(s => s.ShowId).ToList();

            // Count booked seats per show (Confirmed + Held)
            var bookedCounts = await _context.BookingSeats
                .Where(bs => showIds.Contains(bs.ShowId) &&
                             (bs.Status == "Confirmed" || bs.Status == "Held"))
                .GroupBy(bs => bs.ShowId)
                .Select(g => new { ShowId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ShowId, x => x.Count);

            // Min price per show (cheapest seat × multiplier)
            var minPrices = await _context.Seats
                .Where(s => shows.Select(sh => sh.ScreenId).Contains(s.ScreenId) && s.IsActive)
                .GroupBy(s => s.ScreenId)
                .Select(g => new { ScreenId = g.Key, MinPrice = g.Min(s => s.BasePrice) })
                .ToDictionaryAsync(x => x.ScreenId, x => x.MinPrice);

            var viewModels = shows.Select(s => new ShowListViewModel
            {
                ShowId = s.ShowId,
                MovieTitle = s.Movie.Title,
                PosterUrl = _cloudinary.GetResizedUrl(s.Movie.PosterUrl, 150, 225),
                TheatreName = s.Screen.Theatre.Name,
                ScreenName = s.Screen.ScreenName,
                ScreenType = s.Screen.ScreenType,
                City = s.Screen.Theatre.City,
                ShowDate = s.ShowDate,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Language = s.Language,
                PriceMultiplier = s.PriceMultiplier,
                MinPrice = minPrices.ContainsKey(s.ScreenId)
                    ? Math.Round(minPrices[s.ScreenId] * s.PriceMultiplier, 0)
                    : 0,
                TotalSeats = s.Screen.TotalSeats,
                BookedSeats = bookedCounts.ContainsKey(s.ShowId) ? bookedCounts[s.ShowId] : 0,
                Status = s.Status
            }).ToList();

            // Filters for dropdowns
            ViewBag.Movies = await _context.Movies
                .Where(m => m.IsActive)
                .Select(m => new SelectListItem { Value = m.MovieId.ToString(), Text = m.Title })
                .ToListAsync();

            ViewBag.Cities = await _context.Theatres
                .Where(t => t.IsActive)
                .Select(t => t.City).Distinct().OrderBy(c => c).ToListAsync();

            ViewBag.SelectedMovieId = movieId;
            ViewBag.SelectedCity = city;
            ViewBag.SelectedDate = filterDate;

            return View(viewModels);
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Schedule()
        {
            var model = new ShowViewModel();
            await PopulateDropdowns(model);
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Schedule(ShowViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdowns(model);
                return View(model);
            }

            var movie = await _context.Movies.FindAsync(model.MovieId);
            if (movie == null)
            {
                ModelState.AddModelError("MovieId", "Selected Movie not found");
                await PopulateDropdowns(model);
                return View(model);
            }
            if (User.IsInRole("TheatreAdmin"))
            {
                var userIdClaim = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (userIdClaim == null) return Unauthorized();

                var theatreIds = await _context.Theatres
                    .Where(t => t.AdminUserId == userIdClaim.Value)
                    .Select(t => t.TheatreId)
                    .ToListAsync();

                var isValidScreen = await _context.Screens
                    .AnyAsync(s => s.ScreenId == model.ScreenId && theatreIds.Contains(s.TheatreId));

                if (!isValidScreen)
                {
                    ModelState.AddModelError("ScreenId", "You are not allowed to schedule shows for this theatre.");
                    await PopulateDropdowns(model);
                    return View(model);
                }
            }

            var endTime = model.StartTime.Add(TimeSpan.FromMinutes(movie.DurationMinutes));

            var bufferMinutes = 15;
            var bufferedStart = model.StartTime.Subtract(TimeSpan.FromMinutes(bufferMinutes));
            var bufferedEnd = endTime.Add(TimeSpan.FromMinutes(bufferMinutes));

            var hasOverlap = await _context.Shows
                .AnyAsync(s =>
                    s.ScreenId == model.ScreenId &&
                    s.ShowDate.Date == model.ShowDate.Date &&
                    s.Status != "Cancelled" &&
                    s.StartTime < bufferedEnd &&
                    s.EndTime > bufferedStart);

            if (hasOverlap)
            {
                var conflict = await _context.Shows
                    .Include(s => s.Movie)
                    .Where(s =>
                        s.ScreenId == model.ScreenId &&
                        s.ShowDate.Date == model.ShowDate.Date &&
                        s.Status != "Cancelled" &&
                        s.StartTime < bufferedEnd &&
                        s.EndTime > bufferedStart)
                    .FirstOrDefaultAsync();

                var conflictMsg = conflict != null
                    ? $"Conflicts with '{conflict.Movie.Title}' at " +
                    $"{conflict.StartTime:hh\\:mm} - {conflict.EndTime:hh\\:mm}. " +
                    $"A 15-minute gap between shows is required"
                    : "This time slot conflicts with an existing show on this screen";

                ModelState.AddModelError("Conflit", conflictMsg);
                await PopulateDropdowns(model);
                return View(model);
            }

            if (model.ShowDate.Date < DateTime.Today)
            {
                ModelState.AddModelError("ShowDate", "Cannot schedule a show in the past");
                await PopulateDropdowns(model);
                return View(model);
            }

            var show = new Show
            {
                MovieId = model.MovieId,
                ScreenId = model.ScreenId,
                ShowDate = model.ShowDate,
                StartTime = model.StartTime,
                EndTime = endTime,
                Language = model.Language,
                PriceMultiplier = model.PriceMultiplier,
                Status = "Scheduled",
                CreatedAt = DateTime.UtcNow,
            };
            _context.Shows.Add(show);
            await _context.SaveChangesAsync();
            var startDateTime = model.ShowDate.Date + model.StartTime;

            TempData["Success"] = $"Show scheduled '{movie.Title}' on {model.ShowDate:dd MMM yyyy} " +
                                  $"at {startDateTime:hh:mm tt}.";

            return RedirectToAction(nameof(Manage));
        }
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Edit(int id)
        {
            var show = await _context.Shows
                .Include(s => s.Movie)
                .Include(s => s.Screen).ThenInclude(sc => sc.Theatre)
                .FirstOrDefaultAsync(s => s.ShowId == id);

            if (show == null) return NotFound();

            var model = new ShowViewModel
            {
                ShowId = show.ShowId,
                MovieId = show.MovieId,
                ScreenId = show.ScreenId,
                ShowDate = show.ShowDate,
                StartTime = show.StartTime,
                Language = show.Language,
                PriceMultiplier = show.PriceMultiplier,
                MovieTitle = show.Movie.Title,
                MovieDurationMinutes = show.Movie.DurationMinutes
            };

            await PopulateDropdowns(model);
            return View(model);
        }

        [HttpPost,ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Edit(int id,ShowViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdowns(model);
                return View(model);
            }

            var show = await _context.Shows.FindAsync(id);

            if (show == null) return NotFound();

            if (User.IsInRole("TheatreAdmin"))
            {
                var userIdClaim = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (userIdClaim == null) return Unauthorized();

                var theatreIds = await _context.Theatres
                    .Where(t => t.AdminUserId == userIdClaim.Value)
                    .Select(t => t.TheatreId)
                    .ToListAsync();

                var isValidScreen = await _context.Screens
                    .AnyAsync(s => s.ScreenId == model.ScreenId && theatreIds.Contains(s.TheatreId));

                if (!isValidScreen)
                {
                    ModelState.AddModelError("ScreenId", "Unauthorized screen selection.");
                    await PopulateDropdowns(model);
                    return View(model);
                }
            }

            var hasBookings = await _context.Bookings
                .AnyAsync(b => b.ShowId == id && b.Status == "Confirmed");
            
            if (hasBookings && (show.ShowDate != model.ShowDate || show.StartTime != model.StartTime))
            {
                ModelState.AddModelError("",
                    "This show has confirmed bookings. " +
                    "Changing the date/time will affect booked customers. " +
                    "Cancel the show instead if needed."
                    );
                await PopulateDropdowns(model);
                return View(model);
            }

            var movie = await _context.Movies.FindAsync(model.MovieId);
            if (movie == null) return NotFound();

            var endTime = model.StartTime.Add(TimeSpan.FromMinutes(movie.DurationMinutes));

            var bufferMinutes = 15;
            var bufferedStart = model.StartTime.Subtract(TimeSpan.FromMinutes(bufferMinutes));
            var bufferedEnd = endTime.Add(TimeSpan.FromMinutes(bufferMinutes));

            var hasOverlap = await _context.Shows
                .AnyAsync(s =>
                    s.ScreenId == model.ScreenId &&
                    s.ShowDate.Date == model.ShowDate.Date &&
                    s.Status != "Cancelled" &&
                    s.StartTime < bufferedEnd &&
                    s.EndTime > bufferedStart);

            if (hasOverlap)
            {
                ModelState.AddModelError("StartTime", "This time slot conflicts with another show on this screen.");
                await PopulateDropdowns(model);
                return View(model);
            }

            show.MovieId = model.MovieId;
            show.ScreenId = model.ScreenId;
            show.ShowDate = model.ShowDate;
            show.StartTime = model.StartTime;
            show.EndTime = endTime;
            show.Language = model.Language;
            show.PriceMultiplier = model.PriceMultiplier;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Show updated successfully";
            return RedirectToAction(nameof(Manage));
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Cancel(int id)
        {
            var show = await _context.Shows
                .Include(s => s.Movie)
                .FirstOrDefaultAsync(s => s.ShowId == id);

            if (show == null) return NotFound();

            show.Status = "Cancelled";
            await _context.SaveChangesAsync();

            TempData["Success"] =
                $"Show '{show.Movie.Title}' on {show.ShowDate:dd MMM} has been cancelled.";
            return RedirectToAction(nameof(Manage));
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Manage(string? status, DateTime? date)
        {
            var query = _context.Shows
                .Include(s => s.Movie)
                .Include(s => s.Screen).ThenInclude(sc => sc.Theatre)
                .AsQueryable();
            
            if (User.IsInRole("TheatreAdmin"))
            {
                var userIdClaim = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (userIdClaim == null) return Unauthorized();

                var theatreIds = _context.Theatres
                    .Where(t => t.AdminUserId == userIdClaim.Value)
                    .Select(t => t.TheatreId)
                    .ToList();

                query = query.Where(s => theatreIds.Contains(s.Screen.Theatre.TheatreId));
            }

            if (!string.IsNullOrWhiteSpace(status)) 
                query = query.Where(s => s.Status == status);

            if (date.HasValue)
                query = query.Where(s => s.ShowDate.Date == date.Value.Date);
            else
                query = query.Where(s => s.ShowDate >= DateTime.Today.AddDays(-1));

            var shows = await query
                .OrderBy(s => s.ShowDate)
                .ThenBy(s => s.StartTime)
                .ToListAsync();
            ViewBag.StatusFilter = status;
            ViewBag.DateFilter = date?.ToString("yyyy-MMM-dd");
            return View(shows);
            
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> GetScreens(int theatreId)
        {
            var screens = await _context.Screens
                .Where(s => s.TheatreId == theatreId && s.IsActive)
                .Select(s => new
                {
                    id = s.ScreenId,
                    name = $"{s.ScreenName} ({s.ScreenType}) - {s.TotalSeats} seats"
                })
                .ToListAsync();

            return Json(screens);
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> GetMovieInfo(int movieId)
        {
            var movie = await _context.Movies
                .Where(m => m.MovieId == movieId)
                .Select(m => new {
                    duration = m.DurationMinutes,
                    title = m.Title,
                    language = m.Language
                })
                .FirstOrDefaultAsync();

            if (movie == null) return NotFound();
            return Json(movie);
        }
        private async Task PopulateDropdowns(ShowViewModel model)
        {
            model.Movies = await _context.Movies
                .Where(m => m.IsActive)
                .OrderBy(m => m.Title)
                .Select(m => new SelectListItem
                {
                    Value = m.MovieId.ToString(),
                    Text = $"{m.Title} ({m.DurationMinutes} min)"
                })
                .ToListAsync();

            model.Theatres = await _context.Theatres
                .Where(t => t.IsActive)
                .OrderBy(t => t.City).ThenBy(t => t.Name)
                .Select(t => new SelectListItem
                {
                    Value = t.TheatreId.ToString(),
                    Text = $"{t.Name} - {t.City}",
                })
                .ToListAsync();

            if (model.ScreenId >0)
            {
                var screen = await _context.Screens
                    .Include(s => s.Theatre)
                    .FirstOrDefaultAsync(s => s.ScreenId == model.ScreenId);

                if (screen != null)
                {
                    model.Screens = await _context.Screens
                        .Where(s => s.TheatreId == screen.TheatreId && s.IsActive)
                        .Select(s => new SelectListItem
                        {
                            Value = s.ScreenId.ToString(),
                            Text = $"{s.ScreenName} ({s.ScreenName})"
                        })
                        .ToListAsync();
                }
            }
        }
    }
}
