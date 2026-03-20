// =============================================================================
// Models/AppUser.cs
// =============================================================================
// WHY THIS EXISTS:
//   ASP.NET Core Identity already has a built-in "IdentityUser" class that
//   handles email, password hash, phone, lockout, etc. We EXTEND it (inherit)
//   to add our own custom columns like FullName, CreatedAt, IsActive.
//
// HOW IDENTITY WORKS:
//   When you run Add-Migration, EF Core sees that AppUser extends IdentityUser
//   and creates the "AspNetUsers" table with ALL Identity columns PLUS your
//   custom columns in the SAME table. You never create a separate Users table.
//
// TABLE CREATED: AspNetUsers
// =============================================================================

using Microsoft.AspNetCore.Identity;

namespace MovieBooking.Models
{
    public class AppUser : IdentityUser
    {
        // IdentityUser already provides:
        //   - Id (GUID string) — Primary Key
        //   - Email
        //   - PasswordHash
        //   - PhoneNumber
        //   - EmailConfirmed
        //   - PhoneNumberConfirmed
        //   - SecurityStamp, ConcurrencyStamp (internal Identity fields)
        //   - LockoutEnabled, LockoutEnd, AccessFailedCount
        //   - NormalizedEmail, NormalizedUserName (for case-insensitive lookups)

        // ↓ OUR CUSTOM COLUMNS added on top of Identity:

        /// <summary>User's display name shown in UI</summary>
        public string FullName { get; set; } = string.Empty;

        /// <summary>When this account was created</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Soft-delete flag. Set to false instead of deleting the record.
        /// Old bookings still reference this user, so hard deletes would break FK.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public string? RefreshToken { get; set; }

        public DateTime? RefreshTokenExpiryTime { get; set; }

        // ↓ NAVIGATION PROPERTIES — EF Core uses these to JOIN tables
        // They are NOT columns in the database; they represent relationships.

        /// <summary>All bookings made by this user (1 User → Many Bookings)</summary>
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

        /// <summary>Theatres this user administers (if they have TheatreAdmin role)</summary>
        public ICollection<Theatre> AdminTheatres { get; set; } = new List<Theatre>();
    }
}
