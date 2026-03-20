// =============================================================================
// Models/Booking.cs
// =============================================================================
// WHAT THIS IS:
//   Created when a user initiates a booking. Represents the entire booking
//   session. One Booking contains multiple BookingSeat records (one per seat).
//
// STATUS LIFECYCLE:
//   Pending    → user selected seats, has 10 minutes to pay
//   Confirmed  → payment succeeded (triggered by Stripe webhook)
//   Cancelled  → user cancelled OR ExpiresAt passed without payment
//   Refunded   → was Confirmed but then refunded
//
// IMPORTANT — ExpiresAt:
//   When a booking is created (Pending), set ExpiresAt = DateTime.UtcNow + 10 min.
//   A background job (Hangfire or a HostedService) checks every minute and
//   releases any Pending bookings past ExpiresAt → sets their BookingSeats to Released.
//   This prevents seats being held forever if a user abandons checkout.
//
// TABLE CREATED: Bookings
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieBooking.Models
{
    public class Booking
    {
        [Key]
        public int BookingId { get; set; }

        // FKs
        public string UserId { get; set; } = string.Empty;     // FK to AspNetUsers.Id
        public int ShowId { get; set; }

        /// <summary>
        /// Human-readable booking reference shown on ticket: e.g., "CB-2024-A3XK"
        /// Generated using: "CB-" + DateTime.Now.Year + "-" + Guid.NewGuid().ToString()[..4].ToUpper()
        /// Add UNIQUE index on this column in AppDbContext.
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string BookingReference { get; set; } = string.Empty;

        /// <summary>Sum of all BookingSeat.SeatPrice values for this booking</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Platform fee added on top of TotalAmount.
        /// Stripe charges include this — store separately for reporting.
        /// </summary>
        [Column(TypeName = "decimal(8,2)")]
        public decimal ConvenienceFee { get; set; } = 0;

        [MaxLength(20)]
        public string Status { get; set; } = "Pending";     // Pending/Confirmed/Cancelled/Refunded

        public DateTime BookedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Pending bookings must be paid before this time or seats are released.
        /// Set to: BookedAt + 10 minutes
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        // NAVIGATION PROPERTIES
        [ForeignKey("UserId")]
        public AppUser User { get; set; } = null!;

        [ForeignKey("ShowId")]
        public Show Show { get; set; } = null!;

        /// <summary>All seat records for this booking (one per seat selected)</summary>
        public ICollection<BookingSeat> BookingSeats { get; set; } = new List<BookingSeat>();

        /// <summary>The payment record (1:1 — one booking has one payment)</summary>
        public Payment? Payment { get; set; }

        /// <summary>Generated tickets (one per seat)</summary>
        public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    }
}


// =============================================================================
// Models/BookingSeat.cs
// =============================================================================
// WHAT THIS IS:
//   ⚠ THE MOST CRITICAL TABLE IN YOUR SYSTEM ⚠
//
//   Represents ONE seat being held/booked for ONE show.
//   One Booking has many BookingSeats (one per selected seat).
//   One BookingSeat becomes one Ticket after payment.
//
// HOW CONCURRENCY IS PREVENTED (Double Booking Protection):
//   In AppDbContext.OnModelCreating, you MUST add:
//
//   entity.HasIndex(bs => new { bs.ShowId, bs.SeatId })
//         .IsUnique()
//         .HasFilter("[Status] IN ('Held', 'Confirmed')");
//
//   This creates a UNIQUE FILTERED INDEX at the DATABASE LEVEL.
//   If two users try to book the same seat for the same show:
//     - First INSERT succeeds → Status = 'Held'
//     - Second INSERT FAILS with a unique constraint violation
//     - You catch the DbUpdateException and show "Seat no longer available"
//
//   The filter [Status] IN ('Held', 'Confirmed') means:
//     - Released seats CAN be re-booked (their status is 'Released', not in the filter)
//     - Only active holds/confirmed bookings are protected
//
// STATUS LIFECYCLE:
//   Held      → user selected seat, holding during checkout (max 10 min)
//   Confirmed → payment succeeded, seat is permanently booked
//   Released  → hold expired OR booking was cancelled, seat is available again
//
// TABLE CREATED: BookingSeats
// =============================================================================

namespace MovieBooking.Models
{
    public class BookingSeat
    {
        [Key]
        public int BookingSeatId { get; set; }

        // FKs
        public int BookingId { get; set; }
        public int SeatId { get; set; } 
        public int ShowId { get; set; }   // Denormalized (also accessible via Booking.ShowId)
                                           // but needed for the UNIQUE INDEX: (ShowId, SeatId)

        /// <summary>
        /// Price locked in at booking time = Seat.BasePrice × Show.PriceMultiplier.
        /// We MUST snapshot this here — if an admin later changes BasePrice or
        /// PriceMultiplier, old bookings should NOT be affected.
        /// </summary>
        [Column(TypeName = "decimal(8,2)")]
        public decimal SeatPrice { get; set; }

        /// <summary>Held / Confirmed / Released</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Held";

        /// <summary>When the hold was created — used to expire holds after 10 min</summary>
        public DateTime HeldAt { get; set; } = DateTime.UtcNow;

        // NAVIGATION PROPERTIES
        [ForeignKey("BookingId")]
        public Booking Booking { get; set; } = null!;

        [ForeignKey("SeatId")]
        public Seat Seat { get; set; } = null!;

        [ForeignKey("ShowId")]
        public Show Show { get; set; } = null!;

        /// <summary>The ticket generated for this seat after payment (1:1)</summary>
        public Ticket? Ticket { get; set; }
    }
}
