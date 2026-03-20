using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MovieBooking.Web.Models
{
    public class ShowViewModel
    {
        public int ShowId { get; set; }

        [Required(ErrorMessage = "Please select a movie")]
        [Display(Name = "Movie")]
        public int MovieId { get; set; }

        [Required(ErrorMessage = "Please select a theatre")]
        [Display(Name = "Theatre")]
        public int TheatreId { get; set; }

        [Required(ErrorMessage = "Please select a screen")]
        [Display(Name = "Screen")]
        public int ScreenId { get; set; }

        [Required(ErrorMessage = "Show date is required")]
        [Display(Name = "Show Date")]
        [DataType(DataType.Date)]
        public DateTime ShowDate { get; set; } = DateTime.Today.AddDays(1);

        [Required(ErrorMessage = "Start time is required")]
        [Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; } = new TimeSpan(14, 0, 0);

        [Required(ErrorMessage = "Language is required")]
        [MaxLength(50)]
        [Display(Name = "Language")]
        public string Language { get; set; } = "English";

        [Required]
        [Range(0.1, 5.0, ErrorMessage = "Multiplier must be between 0.1 and 5.0")]
        [Display(Name = "Price Multiplier")]
        public decimal PriceMultiplier { get; set; } = 1.0m;

        // Dropdowns
        public List<SelectListItem> Movies { get; set; } = new();
        public List<SelectListItem> Theatres { get; set; } = new();
        public List<SelectListItem> Screens { get; set; } = new();

        //Display heplers
        public int? MovieDurationMinutes { get; set; }
        public string? MovieTitle { get; set; }
        public bool IsEdit => ShowId > 0;
    }

    public class ShowListViewModel
    {
        public int ShowId { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public string? PosterUrl { get; set; }
        public string TheatreName { get; set; } = string.Empty;
        public string ScreenName { get; set; } = string.Empty;
        public string ScreenType { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public DateTime ShowDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Language { get; set; } = string.Empty;
        public decimal PriceMultiplier { get; set; }
        public decimal MinPrice { get; set; }

        public int TotalSeats { get; set; }
        public int BookedSeats { get; set; }
        public int AvailableSeats => TotalSeats - BookedSeats;
        public string Status { get; set; } = string.Empty;
    }

}