using CinemaBooking.Data;
using MovieBooking.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieBooking.Infrastructure.Services;
using MovieBooking.Models;

namespace MovieBooking.Web.Controllers
{
    public class MovieController : Controller
    {
        private readonly AppDbContext _context;
        private readonly CloudinaryService _cloudinary;

        public MovieController(AppDbContext context, CloudinaryService cloudinary)
        {
            this._context = context;
            this._cloudinary = cloudinary;
        }
        public async Task<IActionResult> Index(string? search, string? genre)
        {
            var query = _context.Movies
                .Where(m => m.IsActive)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(m =>
                    m.Title.Contains(search) ||
                    m.Description!.Contains(search));

            if (!string.IsNullOrWhiteSpace(genre))
                query = query.Where(m => m.Genre == genre);

            var movies = await query
                .OrderByDescending(m => m.ReleaseDate)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Genre = genre;

            ViewBag.Genres = await _context.Movies
                .Where(m => m.IsActive && m.Genre != null)
                .Select(m => m.Genre!)
                .Distinct()
                .OrderBy(g => g)
                .ToListAsync();

            ViewBag.Cloudinary = _cloudinary;

            return View(movies);
        }

        public async Task<IActionResult> Detail(int id)
        {
            var movie = await _context.Movies
                .Include(m => m.Shows.Where(s =>
                    s.ShowDate >= DateTime.Today &&
                    s.Status == "Scheduled"))
                    .ThenInclude(s => s.Screen)
                    .ThenInclude(sc => sc.Theatre)
                .FirstOrDefaultAsync(m => m.MovieId == id && m.IsActive);

            if (movie == null)
                return NotFound();

            ViewBag.Cloudinary = _cloudinary;
            return View(movie);
        }

        [Authorize(Roles = "SuperAdmin, TheatreAdmin")]
        public IActionResult Create()
        {
            return View(new MovieViewModel
            {
                ReleaseDate = DateTime.Today
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public  async Task<IActionResult> Create(MovieViewModel model)
        {
            if (model.PosterFile == null || model.PosterFile.Length == 0)
                ModelState.AddModelError("PosterFile", "Please Upload a movie poster");

            if (!ModelState.IsValid)
                return View(model);

            string? posterUrl = null;

            try
            {
                posterUrl = await _cloudinary.UploadPosterAsync(model.PosterFile!, model.Title);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("PosterFile", ex.Message);
                return View(model);
            }

            var movie = new Movie
            {
                Title = model.Title,
                Description = model.Description,
                Genre = model.Genre,
                Language = model.Language,
                DurationMinutes = model.DurationMinutes,
                ReleaseDate = DateTime.Today,
                Rating = model.Rating,
                PosterUrl = posterUrl,
                IsActive = true,
                TrailerUrl = model.TrailerUrl,
                CreatedAt = DateTime.UtcNow
            };

            _context.Movies.Add(movie);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{movie.Title}' has been added successfully";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Edit(int id)
        {
            var movie = await _context.Movies.FindAsync(id);
            if (movie == null) return NotFound();

            var model = new MovieViewModel
            {
                MovieId = movie.MovieId,
                Title = movie.Title,
                Description = movie.Description ?? "",
                Genre = movie.Genre ?? "",
                Language = movie.Language ?? "",
                DurationMinutes = movie.DurationMinutes,
                ReleaseDate = DateTime.Today,
                TrailerUrl = movie.TrailerUrl,
                Rating = movie.Rating,
                ExistingPosterUrl = movie.PosterUrl
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,TheatreAdmin")]
        public async Task<IActionResult> Edit(int id, MovieViewModel model)
        {
            if (id != model.MovieId) return BadRequest();

            if (model.PosterFile == null)
                ModelState.Remove("PosterFile");
            if (!ModelState.IsValid)
                return View(model);

            var movie = await _context.Movies.FindAsync(id);
            if (movie == null) return NotFound();

            if (model.PosterFile != null && model.PosterFile.Length > 0)
            {
                try
                {
                    if (!string.IsNullOrEmpty(movie.PosterUrl))
                        await _cloudinary.DeletePosterAsync(movie.PosterUrl);

                    movie.PosterUrl = await _cloudinary.UploadPosterAsync(model.PosterFile, model.Title);
                }
                catch (Exception ex) 
                {
                    ModelState.AddModelError("PosterFile", ex.Message);
                    model.ExistingPosterUrl = movie.PosterUrl;
                    return View(model);
                }
            }

            movie.Title = model.Title;
            movie.Description = model.Description;
            movie.Genre = model.Genre;
            movie.Language = model.Language;
            movie.DurationMinutes = model.DurationMinutes;
            movie.ReleaseDate = model.ReleaseDate;
            movie.TrailerUrl = model.TrailerUrl;
            movie.Rating = model.Rating;

            await _context.SaveChangesAsync();

            TempData["Sucess"] = $"'{movie.Title}' has been updated";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Delete(int id)
        {
            var movie = await _context.Movies.FindAsync(id);
            if (movie == null) return NotFound();

            movie.IsActive = false;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{movie.Title}' has been removed from listings.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "SuperAdmin, TheatreAdmin")]
        public async Task<IActionResult> Manage()
        {
            var movies = await _context.Movies
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            ViewBag.Cloudinary = _cloudinary;
            return View(movies);
        }
    }
}
