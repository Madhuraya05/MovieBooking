namespace MovieBooking.Web.Models
{
    public class HoldSeatsRequest
    {
        public int ShowId { get; set; }

        public string SelectedSeatIds { get; set; } = string.Empty;

        public List<int> GetSeatIds() =>
            SelectedSeatIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    public class BookingSummaryViewModel
    {
        public int BookingId { get; set;  }
        public string BookingReference { get; set; } = string.Empty;

        public int ShowId { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public string? PosterUrl { get; set; }
        public DateTime ShowDate { get; set;  }
        public TimeSpan StartTime { get; set;  }
        public string TheatreName { get; set; } = string.Empty;
        public string ScreenName { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;

        // Selected seats
        public List<SeatSummaryItem> Seats { get; set; } = new();

        // Pricing
        public decimal SubTotal { get; set; }
        public decimal ConvenienceFee { get; set; }
        public decimal TotalAmount { get; set; }

        // Timer
        public DateTime ExpiresAt { get; set; }

        // Remaining seconds for JS countdown
        public int SecondsRemaining =>
            Math.Max(0, (int)(ExpiresAt - DateTime.UtcNow).TotalSeconds);
    }

    public class SeatSummaryItem
    {
        public int SeatId { get; set; }
        public string SeatLabel { get; set; } = string.Empty; // "A5", "B12"
        public string Category { get; set; } = string.Empty;   // VIP / Premium / Standard
        public decimal Price { get; set; }                      // after multiplier
    }

    // Passed to the seat map view
    public class SeatMapViewModel
    {
        public int ShowId { get; set; }
        public string MovieTitle { get; set; } = string.Empty;
        public string? PosterUrl { get; set; }
        public DateTime ShowDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public string TheatreName { get; set; } = string.Empty;
        public string ScreenName { get; set; } = string.Empty;
        public string ScreenType { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public decimal PriceMultiplier { get; set; }

        // Grouped by row: { "A": [SeatMapItem, ...], "B": [...] }
        public Dictionary<string, List<SeatMapItem>> SeatsByRow { get; set; } = new();
    }

    public class SeatMapItem
    {
        public int SeatId { get; set; }
        public string RowLabel { get; set; } = string.Empty;
        public int SeatNumber { get; set; }
        public string SeatLabel => $"{RowLabel}{SeatNumber}";
        public string Category { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal FinalPrice { get; set; } // BasePrice × PriceMultiplier
        public string Status { get; set; } = "Available"; // Available / Held / Booked
    }

    // User's booking history item
    public class BookingHistoryItem
    {
        public int BookingId { get; set; }
        public string BookingReference { get; set; } = string.Empty;
        public string MovieTitle { get; set; } = string.Empty;
        public string? PosterUrl { get; set; }
        public DateTime ShowDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public string TheatreName { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public int SeatCount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime BookedAt { get; set; }
    }
}
