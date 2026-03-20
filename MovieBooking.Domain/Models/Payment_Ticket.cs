// =============================================================================
// Models/Payment.cs
// =============================================================================
// WHAT THIS IS:
//   Stores the payment record for a booking. Created when Stripe payment succeeds
//   (via webhook, NOT client-side callback). One Booking = One Payment (1:1).
//
// STRIPE FLOW (important to understand before coding):
//   1. User clicks Pay → your backend creates a Stripe PaymentIntent
//   2. Stripe returns a client_secret → sent to frontend
//   3. Frontend Stripe.js collects card → confirms payment
//   4. Stripe calls YOUR webhook endpoint with payment_intent.succeeded event
//   5. In the webhook handler, you:
//      a. Verify the webhook signature (Stripe-Signature header)
//      b. Find the Booking by PaymentIntentId
//      c. Create this Payment record with Status = "Succeeded"
//      d. Update Booking.Status = "Confirmed"
//      e. Update BookingSeats.Status = "Confirmed"
//      f. Generate Ticket records
//      g. Send confirmation email
//
// WHY WEBHOOK NOT CLIENT CALLBACK:
//   The client (browser) can be closed, lose internet, or be tampered with.
//   Stripe webhook is a server-to-server call that is cryptographically signed
//   — you can TRUST it. Always use webhooks for final confirmation.
//
// TABLE CREATED: Payments
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieBooking.Models
{
    public class Payment
    {
        [Key]
        public int PaymentId { get; set; }

        // FK to Booking (1:1 relationship)
        public int BookingId { get; set; }

        /// <summary>
        /// Stripe PaymentIntent ID — format: "pi_3Mz..."
        /// Created BEFORE payment happens (when user starts checkout).
        /// Used to match the webhook event back to this booking.
        /// Store this immediately when you create the PaymentIntent.
        /// </summary>
        [MaxLength(200)]
        public string? StripePaymentIntentId { get; set; }

        /// <summary>
        /// Stripe Charge ID — format: "ch_3Mz..."
        /// Available AFTER payment succeeds. Needed for refunds:
        ///   stripe.Refunds.Create(new RefundCreateOptions { Charge = StripeChargeId })
        /// </summary>
        [MaxLength(200)]
        public string? StripeChargeId { get; set; }

        /// <summary>Total charged (includes convenience fee)</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        /// <summary>Currency code: "inr", "usd", "gbp"</summary>
        [MaxLength(3)]
        public string Currency { get; set; } = "inr";

        /// <summary>Pending / Succeeded / Failed / Refunded</summary>
        [MaxLength(30)]
        public string Status { get; set; } = "Pending";

        /// <summary>card / upi / netbanking / wallet</summary>
        [MaxLength(30)]
        public string? PaymentMethod { get; set; }

        public DateTime? PaidAt { get; set; }       // Set when webhook confirms payment
        public DateTime? RefundedAt { get; set; }   // Set when refund is processed

        [Column(TypeName = "decimal(10,2)")]
        public decimal? RefundAmount { get; set; }

        // NAVIGATION PROPERTY
        [ForeignKey("BookingId")]
        public Booking Booking { get; set; } = null!;
    }
}


// =============================================================================
// Models/Ticket.cs
// =============================================================================
// WHAT THIS IS:
//   The actual cinema ticket. Created AFTER payment is confirmed.
//   One Ticket per seat — if you book 3 seats, you get 3 Ticket records.
//
// QR CODE:
//   QRCodeData stores a string that gets encoded into the QR image.
//   Format: "CB|{TicketCode}|{ShowId}|{SeatId}"
//   Example: "CB|TKT-ABC123|45|201"
//
//   At the gate, staff scan the QR → your system decodes → looks up TicketCode
//   → verifies it's Confirmed, not yet scanned → marks IsScanned = true.
//
// QR LIBRARY: Use QRCoder NuGet package:
//   dotnet add package QRCoder
//   var qrGenerator = new QRCodeGenerator();
//   var qrData = qrGenerator.CreateQrCode(ticketCode, QRCodeGenerator.ECCLevel.Q);
//   var qrCode = new PngByteQRCode(qrData);
//   byte[] qrCodeBytes = qrCode.GetGraphic(20);
//   // Save to cloud or store as base64 in QRCodeData
//
// TABLE CREATED: Tickets
// =============================================================================

namespace MovieBooking.Models
{
    public class Ticket
    {
        [Key]
        public int TicketId { get; set; }

        // FKs
        public int BookingId { get; set; }
        public int BookingSeatId { get; set; }  // Which specific seat this ticket is for

        /// <summary>
        /// Unique code per ticket. Used in QR code and displayed on ticket PDF.
        /// Generate with: "TKT-" + Guid.NewGuid().ToString("N")[..8].ToUpper()
        /// Example: "TKT-A3B4C5D6"
        /// Add UNIQUE index on this in AppDbContext.
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string TicketCode { get; set; } = string.Empty;

        /// <summary>
        /// The string encoded into the QR image.
        /// Format: "CB|{TicketCode}|{ShowId}|{SeatId}"
        /// You can also store the base64 PNG of the QR here if you prefer
        /// not to regenerate it every time.
        /// </summary>
        public string? QRCodeData { get; set; }

        /// <summary>Set to true when the QR is scanned at the gate</summary>
        public bool IsScanned { get; set; } = false;

        /// <summary>When the ticket was scanned (for fraud detection)</summary>
        public DateTime? ScannedAt { get; set; }

        /// <summary>When this ticket record was created (after payment confirmed)</summary>
        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        // NAVIGATION PROPERTIES
        [ForeignKey("BookingId")]
        public Booking Booking { get; set; } = null!;

        [ForeignKey("BookingSeatId")]
        public BookingSeat BookingSeat { get; set; } = null!;
    }
}
