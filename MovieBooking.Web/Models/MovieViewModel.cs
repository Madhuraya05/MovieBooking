// =============================================================================
// ViewModels/MovieViewModel.cs
// =============================================================================
// WHAT THIS IS:
//   Used for the Admin "Add Movie" and "Edit Movie" forms.
//   Key difference from the Movie model:
//     - PosterFile is IFormFile (the uploaded image) NOT a URL string
//     - After upload, we get back a URL and store it in Movie.PosterUrl
//
// IFormFile = ASP.NET's way of receiving an uploaded file from a form.
//   It gives you: FileName, ContentType, Length, and an OpenReadStream() method.
//   You pipe that stream to Cloudinary, get back a URL, save the URL to DB.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MovieBooking.Web.Models
{
    public class MovieViewModel
    {
        public int MovieId { get; set; }  // 0 for new, >0 for edit

        [Required(ErrorMessage = "Title is required")]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Genre is required")]
        [MaxLength(100)]
        public string Genre { get; set; } = string.Empty;

        [Required(ErrorMessage = "Language is required")]
        [MaxLength(50)]
        public string Language { get; set; } = string.Empty;

        [Required(ErrorMessage = "Duration is required")]
        [Range(1, 600, ErrorMessage = "Duration must be between 1 and 600 minutes")]
        [Display(Name = "Duration (minutes)")]
        public int DurationMinutes { get; set; }

        [Required(ErrorMessage = "Release date is required")]
        [Display(Name = "Release Date")]
        [DataType(DataType.Date)]
        public DateTime ReleaseDate { get; set; } = DateTime.Today;

        [MaxLength(500)]
        [Display(Name = "Trailer URL (YouTube)")]
        [Url(ErrorMessage = "Enter a valid URL")]
        public string? TrailerUrl { get; set; }

        [MaxLength(10)]
        [Display(Name = "Age Rating")]
        public string? Rating { get; set; }

        // ↓ The uploaded poster image file
        // [Required] only on Create — on Edit it's optional (keep existing poster)
        [Display(Name = "Movie Poster")]
        public IFormFile? PosterFile { get; set; }

        // ↓ Existing poster URL — shown in edit form so admin sees current poster
        public string? ExistingPosterUrl { get; set; }

        // Helper: is this a create or edit operation?
        public bool IsEdit => MovieId > 0;
    }
}
