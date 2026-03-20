// =============================================================================
// Controllers/SuperAdminController.cs
// =============================================================================
// [Authorize(Roles = "SuperAdmin")] is the key line.
// If a non-SuperAdmin hits any action here, ASP.NET redirects them to
// the AccessDeniedPath configured in Program.cs → /Account/AccessDenied
//
// For now this is a stub. Later you'll add:
//   - Manage all users (list, activate/deactivate, assign roles)
//   - Manage all theatres
//   - View all bookings and revenue
// =============================================================================

using CinemaBooking.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MovieBooking.Models;

//namespace MovieBooking.Controllers
//{
//    [Authorize(Roles = "SuperAdmin")]   // ← ENTIRE controller protected. Every action requires SuperAdmin.
//    public class SuperAdminController : Controller
//    {
//        private readonly UserManager<AppUser> _userManager;

//        public SuperAdminController(UserManager<AppUser> userManager)
//        {
//            _userManager = userManager;
//        }

//        // GET: /SuperAdmin/Dashboard
//        public async Task<IActionResult> Dashboard()
//        {
//            // Pass basic stats to the view using ViewBag (quick and simple)
//            ViewBag.TotalUsers = _userManager.Users.Count();
//            ViewBag.ActiveUsers = _userManager.Users.Count(u => u.IsActive);
//            return View();
//        }

//        // GET: /SuperAdmin/Users — List all users with their roles
//        public async Task<IActionResult> Users()
//        {
//            var users = _userManager.Users.Where(u => u.IsActive).ToList();

//            // Build a dictionary of UserId → list of role names
//            // We need this because GetRolesAsync is async and can't be called in a LINQ query
//            var userRoles = new Dictionary<string, IList<string>>();
//            foreach (var user in users)
//            {
//                userRoles[user.Id] = await _userManager.GetRolesAsync(user);
//            }

//            ViewBag.UserRoles = userRoles;
//            return View(users);
//        }

//        // POST: /SuperAdmin/AssignRole — Assign TheatreAdmin role to a user
//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> AssignTheatreAdmin(string userId)
//        {
//            var user = await _userManager.FindByIdAsync(userId);
//            if (user == null)
//            {
//                TempData["Error"] = "User not found.";
//                return RedirectToAction("Users");
//            }

//            // Remove current roles first (a user should only have one role)
//            var currentRoles = await _userManager.GetRolesAsync(user);
//            await _userManager.RemoveFromRolesAsync(user, currentRoles);

//            // Assign the new role
//            await _userManager.AddToRoleAsync(user, "TheatreAdmin");

//            TempData["Success"] = $"{user.FullName} is now a Theatre Admin.";
//            return RedirectToAction("Users");
//        }
//    }
//}


//// =============================================================================
//// Controllers/TheatreAdminController.cs
//// =============================================================================
//// Accessible only to TheatreAdmin and SuperAdmin.
//// Later you'll add: manage screens, seats, shows for their specific theatre.
//// =============================================================================

namespace MovieBooking.Controllers
{
    [Authorize(Roles = "TheatreAdmin,SuperAdmin")]
    public class TheatreAdminController : Controller
    {
        public IActionResult Dashboard()
        {
            return View();
        }
    }
}

// Controllers/SuperAdminController.cs — UPDATED
// Add movie + theatre counts to Dashboard ViewBag
// Replace your existing SuperAdminController with this

//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.AspNetCore.Mvc;
//using CinemaBooking.Data;
//using CinemaBooking.Models;

namespace MovieBooking.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : Controller
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly AppDbContext _context;

        public SuperAdminController(UserManager<AppUser> userManager, AppDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Dashboard()
        {
            ViewBag.TotalUsers = _userManager.Users.Count();
            ViewBag.ActiveUsers = _userManager.Users.Count(u => u.IsActive);
            ViewBag.TotalMovies = _context.Movies.Count(m => m.IsActive);
            ViewBag.TotalTheatres = _context.Theatres.Count(t => t.IsActive);
            return View();
        }

        public async Task<IActionResult> Users()
        {
            var users = _userManager.Users.Where(u => u.IsActive).ToList();
            var userRoles = new Dictionary<string, IList<string>>();
            foreach (var user in users)
                userRoles[user.Id] = await _userManager.GetRolesAsync(user);

            ViewBag.UserRoles = userRoles;
            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignTheatreAdmin(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) { TempData["Error"] = "User not found."; return RedirectToAction("Users"); }

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, "TheatreAdmin");

            TempData["Success"] = $"{user.FullName} is now a Theatre Admin.";
            return RedirectToAction("Users");
        }
    }
}