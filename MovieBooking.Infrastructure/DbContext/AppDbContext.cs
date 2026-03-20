// =============================================================================
// Data/AppDbContext.cs
// =============================================================================
// WHAT THIS IS:
//   The central class that connects your C# models to your MySQL database.
//   AppDbContext tells EF Core:
//     - What tables exist (DbSet<T> properties)
//     - How tables relate to each other
//     - Any special constraints (unique indexes, decimal precision, etc.)
//
// WHY IdentityDbContext<AppUser>:
//   Instead of inheriting from plain DbContext, we inherit from
//   IdentityDbContext<AppUser>. This automatically adds ALL Identity tables:
//     - AspNetUsers        → your AppUser records
//     - AspNetRoles        → roles (User, TheatreAdmin, SuperAdmin)
//     - AspNetUserRoles    → which users have which roles
//     - AspNetUserClaims   → optional claims per user
//     - AspNetUserLogins   → external logins (Google, Facebook)
//     - AspNetUserTokens   → 2FA tokens, etc.
//   You get ALL of these for FREE just by inheriting IdentityDbContext.
//
// =============================================================================

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MovieBooking.Models;

namespace CinemaBooking.Data
{
    // IdentityDbContext<AppUser> → gives us all Identity tables AND our AppUser extensions
    public class AppDbContext : IdentityDbContext<AppUser>
    {
        // Constructor: receives options (connection string, provider) from Program.cs
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // =====================================================================
        // DbSet<T> = each one becomes a database TABLE
        // You query them like: _context.Movies.Where(...).ToListAsync()
        // =====================================================================

        // Identity tables are added automatically by IdentityDbContext.
        // We only need DbSets for OUR custom tables:

        public DbSet<Movie> Movies => Set<Movie>();
        public DbSet<Theatre> Theatres => Set<Theatre>();
        public DbSet<Screen> Screens => Set<Screen>();
        public DbSet<Seat> Seats => Set<Seat>();
        public DbSet<Show> Shows => Set<Show>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<BookingSeat> BookingSeats => Set<BookingSeat>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Ticket> Tickets => Set<Ticket>();

        // =====================================================================
        // OnModelCreating — configure things that can't be done with attributes
        // =====================================================================
        protected override void OnModelCreating(ModelBuilder builder)
        {
            // CRITICAL: Must call base first so Identity tables are configured
            base.OnModelCreating(builder);
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime) ||
                        property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(
                            new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                                v => v,
                                v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
                            )
                        );
                    }
                }
            }
                // -----------------------------------------------------------------
                // SHOWS — prevent overlapping shows on same screen
                // -----------------------------------------------------------------
                builder.Entity<Show>(entity =>
            {
                // UNIQUE INDEX: A screen cannot have two shows starting at the same time
                // EF Core creates: CREATE UNIQUE INDEX IX_Shows_ScreenId_ShowDate_StartTime
                //                  ON Shows (ScreenId, ShowDate, StartTime)
                entity.HasIndex(s => new { s.ScreenId, s.ShowDate, s.StartTime })
                      .IsUnique()
                      .HasDatabaseName("IX_Shows_Screen_Date_Time");
            });

            // -----------------------------------------------------------------
            // BOOKING SEATS — THE CONCURRENCY GUARD (most important constraint!)
            // -----------------------------------------------------------------
            builder.Entity<BookingSeat>(entity =>
            {
                // ⚠ CRITICAL UNIQUE FILTERED INDEX:
                //
                // Prevents double booking: two users cannot hold/confirm the
                // same seat (SeatId) for the same show (ShowId) at the same time.
                //
                // The filter "Status IN ('Held','Confirmed')" means:
                //   - 'Released' seats are excluded → can be rebooked
                //   - Only active holds and confirmed bookings are protected
                //
                // NOTE: MySQL doesn't support filtered indexes natively in EF Core's
                // HasFilter(). For MySQL use a partial/conditional approach OR
                // handle via application-level transactions + row locking.
                //
                // MYSQL ALTERNATIVE (add in your migration's Up() method manually):
                // migrationBuilder.Sql(
                //   "CREATE UNIQUE INDEX IX_BookingSeats_Concurrency " +
                //   "ON BookingSeats (ShowId, SeatId) " +
                //   "WHERE Status IN ('Held', 'Confirmed');"
                // );
                //
                // For cross-DB compatibility in EF Core, use a regular unique index
                // and handle 'Released' logic in application code:
                entity.HasIndex(bs => new { bs.ShowId, bs.SeatId })
                      .HasDatabaseName("IX_BookingSeats_ShowId_SeatId");
                // In your booking service: before inserting, check no Held/Confirmed
                // record exists for this (ShowId, SeatId) pair inside a transaction.
            });

            // -----------------------------------------------------------------
            // BOOKINGS — unique reference code
            // -----------------------------------------------------------------
            builder.Entity<Booking>(entity =>
            {
                entity.HasIndex(b => b.BookingReference)
                      .IsUnique()
                      .HasDatabaseName("IX_Bookings_Reference");
            });

            // -----------------------------------------------------------------
            // TICKETS — unique ticket code
            // -----------------------------------------------------------------
            builder.Entity<Ticket>(entity =>
            {
                entity.HasIndex(t => t.TicketCode)
                      .IsUnique()
                      .HasDatabaseName("IX_Tickets_Code");
            });

            // -----------------------------------------------------------------
            // PAYMENT — 1:1 with Booking
            // EF Core needs help knowing which side "owns" the FK in a 1:1
            // -----------------------------------------------------------------
            builder.Entity<Payment>(entity =>
            {
                entity.HasOne(p => p.Booking)
                      .WithOne(b => b.Payment)
                      .HasForeignKey<Payment>(p => p.BookingId)
                      .OnDelete(DeleteBehavior.Restrict);  // Don't cascade-delete payments
            });

            // -----------------------------------------------------------------
            // BOOKING → BOOKINGSEATS → SEAT
            // Configure cascade delete behavior
            // -----------------------------------------------------------------
            builder.Entity<BookingSeat>(entity =>
            {
                // If Booking is deleted, delete its BookingSeats
                entity.HasOne(bs => bs.Booking)
                      .WithMany(b => b.BookingSeats)
                      .HasForeignKey(bs => bs.BookingId)
                      .OnDelete(DeleteBehavior.Cascade);

                // If Seat is deleted, RESTRICT (don't cascade — protect booking history)
                entity.HasOne(bs => bs.Seat)
                      .WithMany(s => s.BookingSeats)
                      .HasForeignKey(bs => bs.SeatId)
                      .OnDelete(DeleteBehavior.Restrict);

                // ShowId on BookingSeat — Restrict deletion (can't delete a show with bookings)
                entity.HasOne(bs => bs.Show)
                      .WithMany(s => s.BookingSeats)
                      .HasForeignKey(bs => bs.ShowId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // -----------------------------------------------------------------
            // THEATRE → AppUser (AdminUser)
            // A user can be deleted without deleting their theatre (set null)
            // -----------------------------------------------------------------
            builder.Entity<Theatre>(entity =>
            {
                entity.HasOne(t => t.AdminUser)
                      .WithMany(u => u.AdminTheatres)
                      .HasForeignKey(t => t.AdminUserId)
                      .OnDelete(DeleteBehavior.SetNull);  // If admin user deleted, set null
            });

            // -----------------------------------------------------------------
            // SHOWS → BOOKINGS
            // Restrict: can't delete a show that has bookings
            // -----------------------------------------------------------------
            builder.Entity<Booking>(entity =>
            {
                entity.HasOne(b => b.Show)
                      .WithMany(s => s.Bookings)
                      .HasForeignKey(b => b.ShowId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }   
    }
}
