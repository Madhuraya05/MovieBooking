using CinemaBooking.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieBooking.Models;
using MovieBooking.Web.Models;
using Org.BouncyCastle.Security;

namespace MovieBooking.Web.Controllers
{
    public class TheatreController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;

        public TheatreController(AppDbContext context,UserManager<AppUser> userManager)
        {
            this._context = context;
            this._userManager = userManager;
        }


        public async Task<IActionResult> Index(string? city)
        {
            var query = _context.Theatres
                .Where(t => t.IsActive)
                .Include(t => t.Screens.Where(s => s.IsActive))
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(city))
            {
                query = query.Where(t => t.City == city);
            }

            var theatres= await query.OrderBy(t => t.City).ThenBy(t => t.Name).ToListAsync();

            ViewBag.Cities = await _context.Theatres
                .Where(t => t.IsActive)
                .Select(t => t.City)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            ViewBag.SelectedCity = city;
            return View(theatres);
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public IActionResult Create() => View(new TheatreViewModel());

        [HttpPost,ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Create(TheatreViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var userId = _userManager.GetUserId(User);

            var theatre = new Theatre()
            {
                Name = model.Name,
                City = model.City,
                Address = model.Address,
                PhoneNumber = model.PhoneNumber,
                AdminUserId = userId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            _context.Theatres.Add(theatre);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{theatre.Name} has been created! Now add screens to it";
            return RedirectToAction(nameof(Screens),new {id  = theatre.TheatreId});
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Edit(int id)
        {
            var theatre = await _context.Theatres.FindAsync(id);
            if (theatre == null) return NotFound();

            if (User.IsInRole("TheatreAdmin") && theatre.AdminUserId != _userManager.GetUserId(User))
                return Forbid();

            return View(new TheatreViewModel
            {
                TheatreId = theatre.TheatreId,
                Name = theatre.Name,
                City =  theatre.City,
                Address = theatre.Address ?? "",
                PhoneNumber = theatre.PhoneNumber,
            });            
        }

        [HttpPost,ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Edit(int id, TheatreViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var theatre = await _context.Theatres.FindAsync(id);
            if (theatre == null) return NotFound();

            if (User.IsInRole("TheatreAdmin") && theatre.AdminUserId != _userManager.GetUserId(User))
                return Forbid();

            theatre.Name = model.Name;
            theatre.City = model.City;
            theatre.Address = model.Address;
            theatre.PhoneNumber = model.PhoneNumber;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Theatre upload successfully";
            return RedirectToAction(nameof(Screens),new {id});
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Screens(int id )
        {
            var theatre = await _context.Theatres
                .Include(t => t.Screens.Where(s => s.IsActive))
                .FirstOrDefaultAsync(t => t.TheatreId == id);

            if (theatre == null) return NotFound();

            if (User.IsInRole("TheatreAdmin") && theatre.AdminUserId != _userManager.GetUserId(User))
                return Forbid() ;

            var seatCounts = await _context.Seats
                .Where(s => theatre.Screens.Select(sc => sc.ScreenId).Contains(s.ScreenId) && s.IsActive)
                .GroupBy(s => s.ScreenId)
                .Select(g => new { ScreenId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ScreenId, x => x.Count);

            ViewBag.SeatCounts = seatCounts;
            ViewBag.Theatre = theatre;
            return View(theatre.Screens.ToList());
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> AddScreen(int id)
        {
            var theatre = await _context.Theatres.FindAsync(id);
            if (theatre == null) return NotFound();

            ViewBag.Theatre = theatre;
            return View(new ScreenViewModel { TheatreId = id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> AddScreen(int id, ScreenViewModel model)
        {
            var theatre = await _context.Theatres.FindAsync(id);
            if (theatre == null) return NotFound();
            ViewBag.Theatre = theatre;

            if (!ModelState.IsValid) return View(model);

            if (model.VipRows + model.PremiumRows > model.Rows)
            {
                ModelState.AddModelError("",
                    $"VIP rows ({model.VipRows}) + Premium rows ({model.PremiumRows})" +
                    $"can not exceed total rows ({model.Rows})"
                    );
                return View(model);
            }

            var screen = new Screen
            {
                TheatreId = id,
                ScreenName = model.ScreenName,
                ScreenType = model.ScreenType,
                TotalSeats = model.TotalSeats,
                IsActive = true
            };
            _context.Screens.Add(screen);
            await _context.SaveChangesAsync();

            var seats = new List<Seat>();

            for (int row = 0; row < model.Rows; row++)
            {
                string rowLabel = ((char)('A' + row)).ToString();

                string category;
                int price;

                if (row < model.VipRows)
                {
                    category = "VIP";
                    price = model.VipPrice;
                }
                else if (row < model.VipRows + model.PremiumRows)
                {
                    category = "Premium";
                    price = model.PremiumPrice;
                }
                else
                {
                    category = "Standard";
                    price = model.StandardPrice;
                }
                for (int seatNum = 1; seatNum <= model.SeatsPerRow; seatNum++)
                {
                    seats.Add(new Seat
                    {
                        ScreenId = screen.ScreenId,
                        RowLabel = rowLabel,
                        SeatNumber = seatNum,
                        Category = category,
                        BasePrice = price,
                        IsActive = true
                    }
                    );
                }
            }

            await _context.Seats.AddRangeAsync( seats );
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{screen.ScreenName}' created with {seats.Count} seats " +
                $"({model.VipRows} VIP rows , {model.PremiumRows} Premium rows, " +
                $"{model.Rows - model.VipRows - model.PremiumRows} Standard rows )";

            return RedirectToAction(nameof(Screens), new { id });
        
        }

        [Authorize(Roles = "SuperAdmin, TheatreAdmin")]
        public async Task<IActionResult> SeatLayout(int id)
        {
            var screen = await _context.Screens
                .Include(s => s.Theatre)
                .Include(s => s.Seats.Where(seat => seat.IsActive))
                .FirstOrDefaultAsync(s => s.ScreenId == id);

            if (screen == null) return NotFound();

            var seatsByRow = screen.Seats
                .OrderBy(s => s.RowLabel)
                .ThenBy(s => s.SeatNumber)
                .GroupBy(s => s.Category)
                .ToDictionary(g => g.Key , g => g.ToList());

            ViewBag.Screen = screen;
            ViewBag.Theatre = screen.Theatre;
            return View(seatsByRow);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> DeleteTheatre(int id)
        {
            var theatre = await _context.Theatres.FindAsync(id);
            if (theatre == null) return NotFound();

            theatre.IsActive = false;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{theatre.Name}' has been deactivated";
            return RedirectToAction(nameof(Manage));
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> DeleteScreen(int screenId, int theatreId)
        {
            var screen = await _context.Screens.FindAsync(screenId);
            if (screen == null) return NotFound();

            screen.IsActive = false;
            var seats = await _context.Seats
                .Where(s => s.ScreenId == screenId)
                .ToListAsync();
            seats.ForEach(s => s.IsActive = false);

            await _context.SaveChangesAsync();
            TempData["Success"] = $"'{screen.ScreenName}' has been removed";
            return RedirectToAction(nameof(Screens), new { id = theatreId });
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Manage()
        {
            IQueryable<Theatre> query;

            if (User.IsInRole("SuperAdmin"))
            {
                query = _context.Theatres
                    .Include(t => t.Screens)
                    .OrderByDescending(t => t.CreatedAt);
            }
            else
            {
                var userId = _userManager.GetUserId(User);
                query = _context.Theatres
                    .Include(t => t.Screens)
                    .Where(t => t.AdminUserId == userId);
            }

            return View(await query.ToListAsync());
        }
    }
}
