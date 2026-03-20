// =============================================================================
// Models/Theatre.cs
// =============================================================================
// WHAT THIS IS:
//   A physical cinema building. One Theatre has multiple Screens inside it.
//   Each Theatre is managed by a TheatreAdmin user.
//
// RELATIONSHIP:
//   AppUser (admin) ──< Theatre  (one admin can manage many theatres)
//   Theatre ──< Screens          (one theatre has many screens)
//
// TABLE CREATED: Theatres
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieBooking.Models
{
    public class Theatre
    {
        [Key]
        public int TheatreId { get; set; }

        // ↓ FOREIGN KEY — links to AppUser.Id (the admin who manages this theatre)
        // [ForeignKey("AdminUser")] tells EF Core that "AdminUserId" is the FK
        // for the "AdminUser" navigation property below.
        public string? AdminUserId { get; set; }   // string because IdentityUser.Id is string (GUID)

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        public string? Address { get; set; }

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ↓ NAVIGATION PROPERTIES
        [ForeignKey("AdminUserId")]
        public AppUser? AdminUser { get; set; }        // The admin who owns this theatre

        public ICollection<Screen> Screens { get; set; } = new List<Screen>();
    }
}


// =============================================================================
// Models/Screen.cs
// =============================================================================
// WHAT THIS IS:
//   A specific screening room inside a Theatre (e.g., "Screen 1", "IMAX Hall").
//   Each Screen has a fixed set of Seats. Shows are scheduled on a Screen.
//
// RELATIONSHIP:
//   Theatre ──< Screen  (one theatre has many screens)
//   Screen ──< Seats    (one screen has many seats)
//   Screen ──< Shows    (many shows are scheduled on a screen over time)
//
// TABLE CREATED: Screens
// =============================================================================

namespace MovieBooking.Models
{
    public class Screen
    {
        [Key]
        public int ScreenId { get; set; }

        // FK to Theatre — every screen belongs to one theatre
        public int TheatreId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ScreenName { get; set; } = string.Empty;  // e.g., "Screen 1", "Gold Hall"

        /// <summary>
        /// Denormalized count for quick display. Must equal the actual count
        /// of Seat records for this screen. Update this when seats are added/removed.
        /// </summary>
        public int TotalSeats { get; set; }

        /// <summary>Standard, IMAX, 4DX, Dolby</summary>
        [MaxLength(20)]
        public string ScreenType { get; set; } = "Standard";

        public bool IsActive { get; set; } = true;

        // NAVIGATION PROPERTIES
        [ForeignKey("TheatreId")]
        public Theatre Theatre { get; set; } = null!;  // null! = "I guarantee this won't be null at runtime"

        public ICollection<Seat> Seats { get; set; } = new List<Seat>();
        public ICollection<Show> Shows { get; set; } = new List<Show>(); 
    }
}


// =============================================================================
// Models/Seat.cs
// =============================================================================
// WHAT THIS IS:
//   A single physical seat in a Screen. Created once when the screen is set up.
//   Seats are NEVER deleted — only deactivated with IsActive = false.
//
//   Example: Screen 1 has rows A–J, with seats 1–20 per row = 200 Seat records.
//
// RELATIONSHIP:
//   Screen ──< Seat        (one screen has many seats)
//   Seat ──< BookingSeats  (a seat can be booked across many shows)
//
// PRICING:
//   BasePrice lives here (e.g., VIP = 500 rs, Standard = 200 rs).
//   The final price = BasePrice × Show.PriceMultiplier, stored in BookingSeats.SeatPrice.
//
// TABLE CREATED: Seats
// =============================================================================

namespace MovieBooking.Models
{
    public class Seat
    {
        [Key]
        public int SeatId { get; set; }

        // FK to Screen
        public int ScreenId { get; set; }

        /// <summary>
        /// The row label: A, B, C, ... Z, AA, AB, etc.
        /// Users see this as "Row A, Seat 5" → A5
        /// </summary>
        [Required]
        [MaxLength(3)]
        public string RowLabel { get; set; } = string.Empty;

        /// <summary>Seat number within the row (1, 2, 3, ...)</summary>
        public int SeatNumber { get; set; }

        /// <summary>
        /// Seat category for dynamic pricing:
        ///   Standard = cheapest
        ///   Premium   = middle tier
        ///   VIP       = most expensive
        ///   Recliner  = luxury
        /// </summary>
        [MaxLength(20)]
        public string Category { get; set; } = "Standard";

        /// <summary>
        /// Base price for this seat category.
        /// [Column(TypeName = "decimal(8,2)")] is REQUIRED for EF Core to map
        /// C# decimal to MySQL DECIMAL(8,2) — without this, MySQL uses DOUBLE
        /// which has floating-point rounding errors (never use for money!).
        /// </summary>
        [Column(TypeName = "decimal(8,2)")]
        public decimal BasePrice { get; set; }

        public bool IsActive { get; set; } = true;

        // NAVIGATION PROPERTIES
        [ForeignKey("ScreenId")]
        public Screen Screen { get; set; } = null!;

        public ICollection<BookingSeat> BookingSeats { get; set; } = new List<BookingSeat>();
    }
}
