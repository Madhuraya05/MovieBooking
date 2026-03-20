// =============================================================================
// Models/Show.cs
// =============================================================================
// WHAT THIS IS:
//   A scheduled screening of a Movie on a specific Screen at a specific date/time.
//   This is the JOIN point between Movies and Screens.
//
//   Example: "Avengers on Screen 2, 15th March 2024 at 6:30 PM"
//   = Show { MovieId=5, ScreenId=2, ShowDate=2024-03-15, StartTime=18:30 }
//
// RELATIONSHIP:
//   Movie  ──< Show    (one movie can have many shows)
//   Screen ──< Show    (one screen can host many shows on different dates/times)
//   Show   ──< Booking (users book shows)
//
// IMPORTANT CONSTRAINT (add in AppDbContext.OnModelCreating):
//   UNIQUE INDEX on (ScreenId, ShowDate, StartTime) — prevents scheduling
//   two shows at the same time in the same screen (overlap prevention).
//
// TABLE CREATED: Shows
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieBooking.Models
{
    public class Show
    {
        [Key]
        public int ShowId { get; set; }

        // FKs — both required
        public int MovieId { get; set; }
        public int ScreenId { get; set; }

        /// <summary>The calendar date of this show (2024-03-15)</summary>
        public DateTime ShowDate { get; set; }

        /// <summary>
        /// Show start time (18:30:00).
        /// TimeSpan maps to MySQL TIME column.
        /// </summary>
        public TimeSpan StartTime { get; set; }

        /// <summary>
        /// Calculated as StartTime + Movie.DurationMinutes.
        /// Store it so you can query "is there any show between X and Y on this screen?"
        /// </summary>
        public TimeSpan EndTime { get; set; }

        /// <summary>Language of this show: English, Hindi, Tamil, etc.</summary>
        [MaxLength(50)]
        public string Language { get; set; } = "English";

        /// <summary>
        /// Multiplier applied to seat BasePrice for this show.
        /// 1.0 = normal price
        /// 1.5 = weekend / peak pricing
        /// 0.8 = morning discount show
        ///
        /// Final price = Seat.BasePrice × PriceMultiplier (saved in BookingSeat.SeatPrice)
        /// </summary>
        [Column(TypeName = "decimal(4,2)")]
        public decimal PriceMultiplier { get; set; } = 1.0m;

        /// <summary>
        /// Show lifecycle status:
        ///   Scheduled = upcoming, booking open
        ///   Live      = currently playing
        ///   Completed = finished
        ///   Cancelled = cancelled (no new bookings, existing get refunded)
        /// </summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Scheduled";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // NAVIGATION PROPERTIES
        [ForeignKey("MovieId")]
        public Movie Movie { get; set; } = null!;

        [ForeignKey("ScreenId")]
        public Screen Screen { get; set; } = null!;

        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public ICollection<BookingSeat> BookingSeats { get; set; } = new List<BookingSeat>();
    }
}
