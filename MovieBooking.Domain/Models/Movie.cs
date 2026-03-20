// =============================================================================
// Models/Movie.cs
// =============================================================================
// WHAT THIS IS:
//   Represents a film in the system. An admin creates a Movie record once,
//   and then multiple Shows can be scheduled for that movie across different
//   theatres and screens.
//
// RELATIONSHIP:
//   Movie ──< Shows  (one movie can have many scheduled shows)
//
// TABLE CREATED: Movies
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieBooking.Models
{
    public class Movie
    {
        // [Key] tells EF Core this is the Primary Key.
        // By convention, a property named "Id" or "MovieId" is auto-detected as PK,
        // but we write [Key] explicitly for clarity.
        [Key]
        public int MovieId { get; set; }

        // [Required] → NOT NULL in the database
        // [MaxLength(200)] → VARCHAR(200) — also enforced in Razor form validation
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }          // TEXT, nullable (?)

        [MaxLength(100)]
        public string? Genre { get; set; }

        [MaxLength(50)]
        public string? Language { get; set; }

        /// <summary>How long the movie runs in minutes (e.g. 148)</summary>
        public int DurationMinutes { get; set; }

        public DateTime ReleaseDate { get; set; }

        /// <summary>
        /// URL to the poster image stored in cloud storage (Cloudinary/S3/Azure Blob).
        /// We store the URL as a string, not the actual image bytes.
        /// </summary>
        [MaxLength(500)]
        public string? PosterUrl { get; set; }

        /// <summary>YouTube or direct URL to the trailer</summary>
        [MaxLength(500)]
        public string? TrailerUrl { get; set; }

        /// <summary>Age rating: G, PG, PG-13, R, NC-17</summary>
        [MaxLength(10)]
        public string? Rating { get; set; }

        /// <summary>
        /// Soft-delete: set false to hide movie without breaking Show/Booking history.
        /// Never hard-delete a movie that has past shows.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ↓ NAVIGATION PROPERTY
        // This does NOT create a column. EF Core uses it to:
        //   1. Build the JOIN when you do .Include(m => m.Shows)
        //   2. Know the relationship for foreign key setup in Shows table
        public ICollection<Show> Shows { get; set; } = new List<Show>();
    }
}
