using System.ComponentModel.DataAnnotations;

namespace MovieBooking.Web.Models
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Full name is required")]
        [MaxLength(100)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Enter a Valid email address")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Phone number is required")]
        [Phone]
        [Display(Name = "Phone Number")]
        public string PhoneNumber { get ; set; }  = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [MinLength(8,ErrorMessage = "Password must be atlaest 8 character")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set;} = string.Empty;

        [Required(ErrorMessage = "Please confirm your Password")]
        [DataType(DataType.Password)]
        [Compare("Password",ErrorMessage = "Password do not match")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class LoginViewModel
    {
        [Required(ErrorMessage = "Email is Required")]
        [EmailAddress(ErrorMessage = "Enter a valid Email Adress")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        
        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; } = false;
    }
}






// ViewModels/RegisterViewModel.cs
// =============================================================================
// WHAT IS A VIEWMODEL?
//   A ViewModel is NOT a database model. It's a class that carries data
//   between your Controller and your Razor View. It only contains what
//   the form needs — nothing more.
//
//   Why not use AppUser directly in the form?
//   - AppUser has sensitive fields (PasswordHash, SecurityStamp) you don't want exposed
//   - AppUser doesn't have a "ConfirmPassword" field
//   - ViewModels let you add display annotations without polluting your DB model
// =============================================================================