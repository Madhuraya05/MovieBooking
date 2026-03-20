using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MovieBooking.Web.Models
{
    public class TheatreViewModel
    {
        public int TheatreId { get; set;  }

        [Required(ErrorMessage = "Theatre name is required")]
        [MaxLength(100)]
        [Display(Name = "Theatre Name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "City is required")]
        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [Required(ErrorMessage = "Address is required")]
        [Display(Name = "Full Address")]
        public string Address { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Phone Number")]
        public string? PhoneNumber { get; set;  }

        public bool IsEdit => TheatreId > 0;
    }

    public class ScreenViewModel
    {
        public int ScreenId { get; set; }
        public int TheatreId { get; set; }

        [Required(ErrorMessage = "Screen name is required")]
        [MaxLength(100)]
        [Display(Name = "Screen Name")]
        public string ScreenName { get; set;  } = string.Empty ;

        [Required]
        [Display(Name = "Screen Type")]
        public string ScreenType { get; set; } = "Standard";

        [Required]
        [Range(1, 26, ErrorMessage = "Rows must be between 1 and 26")]
        [Display(Name = "Number of Rows")]
        public int Rows { get; set; } = 10;

        [Required]
        [Range(1,50,ErrorMessage = "Seats per row must be between 1 and 50")]
        [Display(Name = "Seats Per Row")]
        public int SeatsPerRow { get; set; } = 15;

        [Required]
        [Range(1, 10000)]
        [Display(Name = "Standard Price (₹)")]
        public int StandardPrice { get; set; } = 150;

        [Required]
        [Range(1, 10000)]
        [Display(Name = "Primium Price (₹)")]
        public int PremiumPrice { get; set; } = 250;

        [Required]
        [Range(1, 10000)]
        [Display(Name = "Primium Price (₹)")]
        public int VipPrice { get; set; } = 400;

        [Required]
        [Range(1, 10)]
        [Display(Name = "VIP Rows (from front)")]
        public int VipRows { get; set; } = 2;

        [Range(0, 10)]
        [Display(Name = "Premium Rows")]
        public int PremiumRows { get; set; } = 3;

        public bool IsEdit => ScreenId > 0;

        // Read-only helper: total seats
        public int TotalSeats => Rows * SeatsPerRow;
    }
}
