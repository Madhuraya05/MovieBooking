using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MovieBooking.Application.DTOs;
using MovieBooking.Infrastructure.Services.Interfaces;
using MovieBooking.Models;
using MovieBooking.Web.Models;

namespace MovieBooking.Web.Controllers
{
    
    public class AccountController : Controller
    {
        private readonly UserManager<AppUser> userManager;
        private readonly SignInManager<AppUser> signInManager;
        private readonly RoleManager<IdentityRole> roleManager;

        public AccountController(UserManager<AppUser> userManager,SignInManager<AppUser> signInManager,RoleManager<IdentityRole> roleManager) 
        {
            this.userManager = userManager;
            this.signInManager = signInManager;
            this.roleManager = roleManager;
        }
        /// <summary>
        /// default view
        /// </summary>
        /// <returns></returns>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// the get register which return register view
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IActionResult Register()
        {
            if (signInManager.IsSignedIn(User))
                return RedirectToAction("Index", "Home");

            return View();
        }
        /// <summary>
        /// this post method store the user in db
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = new AppUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            var result = await userManager.CreateAsync(user,model.Password);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, "User");

                TempData["Success"] = "Account created successfully, please log in";
                return RedirectToAction("Login");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }

            return View(model);
        }

        /// <summary>
        /// get view for login
        /// </summary>
        /// <param name="returnUrl"></param>
        /// <returns></returns>
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (signInManager.IsSignedIn(User))
                return RedirectToAction("Index", "Home");

            ViewData["ReturnUrl"] = returnUrl;

            return View();
        }

        /// <summary>
        /// checking user deatil for login
        /// </summary>
        /// <param name="model"></param>
        /// <param name="returnUrl"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(model);

            var result = await signInManager.PasswordSignInAsync(
                model.Email,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true
                );
            
            if (result.Succeeded)
            {
                var user = await userManager.FindByEmailAsync(model.Email);

                if (user != null && !user.IsActive)
                {
                    await signInManager.SignOutAsync();
                    ModelState.AddModelError("", "Your account has been deactivated. contact support");
                    return View(model);
                }

                if (user!= null)
                {
                    if (await userManager.IsInRoleAsync(user, "SuperAdmin"))
                        return RedirectToAction("Dashboard", "SuperAdmin");

                    if (await userManager.IsInRoleAsync(user, "TheatreAdmin"))
                        return RedirectToAction("Dashboard", "TheatreAdmin");

                }

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Home");
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError("", "Account locked due to too many failed attempts. Try again in 15 minutes.");
                return View(model);
            }

            if (result.IsNotAllowed)
            {
                ModelState.AddModelError("", "Please confirm your email before logging in.");
                return View(model);
            }

            
            ModelState.AddModelError("", "Invalid email or password.");
            return View(model);
        }

        /// <summary>
        /// logout user using signInManager
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
