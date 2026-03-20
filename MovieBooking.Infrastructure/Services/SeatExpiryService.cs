using CinemaBooking.Data;
using CloudinaryDotNet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace MovieBooking.Infrastructure.Services
{
    public class SeatExpiryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SeatExpiryService> _logger;

        public SeatExpiryService(IServiceScopeFactory scopeFactory, ILogger<SeatExpiryService> logger) 
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SeatExpiryService started.");

            // Run immediately on startup, then every 60 seconds
            while (!stoppingToken.IsCancellationRequested)
            {
                await ReleaseExpiredSeats();
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }

        private async Task ReleaseExpiredSeats()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;

                var expiredBookings = await context.Bookings
                    .Include(b => b.BookingSeats)
                    .Where(b => b.Status == "Pending" && b.ExpiresAt < now)
                    .ToListAsync();

                if (!expiredBookings.Any()) return;

                foreach (var booking in expiredBookings)
                {
                    foreach (var seat in booking.BookingSeats.Where(bs => bs.Status == "Held"))
                        seat.Status = "Released";

                    booking.Status = "Cancelled";
                }

                await context.SaveChangesAsync();

                _logger.LogInformation(
                    "Released {Count} expired bookings at {Time}.",
                    expiredBookings.Count, now);

            } catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SeatExpiryService");
            }
        }
    }
}


