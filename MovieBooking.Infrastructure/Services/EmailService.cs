using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using QRCoder;
using CinemaBooking.Data;
using MovieBooking.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CinemaBooking.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;
        private readonly IConfiguration configuration;

        public EmailService(
            ILogger<EmailService> logger,IConfiguration configuration)
        {
            _logger = logger;
            this.configuration = configuration;
        }

        // ─────────────────────────────────────────────────────────────────────
        // MAIN METHOD: Send Booking Confirmation with QR Codes
        // Called after payment is confirmed (in ConfirmBooking or webhook)
        // ─────────────────────────────────────────────────────────────────────
        public async Task SendBookingConfirmationAsync(
            string toEmail,
            string toName,
            BookingEmailData data)
        {
            try
            {
                var message = new MimeMessage();

                // From
                message.From.Add(new MailboxAddress(
                    configuration["Email:FromName"], configuration["Email:FromAddress"]));

                // To
                message.To.Add(new MailboxAddress(toName, toEmail));

                message.Subject = $"🎬 Booking Confirmed — {data.MovieTitle} | {data.BookingReference}";

                // Build email body with embedded QR codes
                var builder = new BodyBuilder();

                // Generate QR code images and embed them
                var qrImages = new List<(string Cid, byte[] Bytes, string TicketCode)>();

                foreach (var ticket in data.Tickets)
                {
                    var qrBytes = GenerateQrCode(ticket.TicketCode);
                    var cid = $"qr_{ticket.TicketCode}";
                    qrImages.Add((cid, qrBytes, ticket.TicketCode));

                    // Add as linked resource (embedded image in email)
                    var image = builder.LinkedResources.Add(
                        $"{ticket.TicketCode}.png",
                        qrBytes,
                        new ContentType("image", "png"));
                    image.ContentId = cid;
                    image.ContentDisposition = new ContentDisposition(
                        ContentDisposition.Inline);
                }

                // Build HTML email
                builder.HtmlBody = BuildEmailHtml(data, qrImages);

                // Plain text fallback for email clients that don't render HTML
                builder.TextBody = BuildEmailText(data);

                message.Body = builder.ToMessageBody();

                // Send via Gmail SMTP
                using var client = new SmtpClient();

                // Connect to Gmail SMTP
                await client.ConnectAsync(
                    configuration["Email:Host"],
                    int.Parse(configuration["Email:Port"]),
                    SecureSocketOptions.StartTls);

                // Authenticate with App Password
                await client.AuthenticateAsync(
                    configuration["Email:Username"],
                    configuration["Email:Password"]);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation(
                    "Booking confirmation email sent to {Email} for {Reference}",
                    toEmail, data.BookingReference);
            }
            catch (Exception ex)
            {
                // Log but don't throw — email failure should NOT break the booking
                _logger.LogError(ex,
                    "Failed to send booking confirmation email to {Email}",
                    toEmail);
            }
        }
        /// <summary>
        /// 
        /// 
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
     
        // ─────────────────────────────────────────────────────────────────────
        // GENERATE QR CODE
        // Encodes the ticket code as a PNG QR image (byte array)
        // ─────────────────────────────────────────────────────────────────────
        private static byte[] GenerateQrCode(string content)
        {
            // QRCoder generates the QR matrix
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);

            // PngByteQRCode renders it as PNG bytes (no System.Drawing dependency)
            var qrCode = new PngByteQRCode(qrData);

            // pixelsPerModule=10 → each QR module is 10×10 pixels → ~330×330px image
            // darkColorRgba/lightColorRgba set QR colors
            return qrCode.GetGraphic(
                pixelsPerModule: 10,
                darkColorRgba: new byte[] { 15, 15, 20, 255 },   // near-black
                lightColorRgba: new byte[] { 255, 255, 255, 255 } // white
            );
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="qrImages"></param>
        /// <returns></returns>
        private static string BuildEmailHtml(
            BookingEmailData data,
            List<(string Cid, byte[] Bytes, string TicketCode)> qrImages)
        {
            var seatsHtml = string.Join("", data.Tickets.Select((t, i) =>
            {
                var qr = qrImages.FirstOrDefault(q => q.TicketCode == t.TicketCode);
                var catColor = t.Category switch
                {
                    "VIP" => "#e8344a",
                    "Premium" => "#6366f1",
                    _ => "#10b981"
                };
                var catBg = t.Category switch
                {
                    "VIP" => "#fff0f2",
                    "Premium" => "#eef2ff",
                    _ => "#f0fdf4"
                };

                return $@"
                <div style='background:#f8f9fc;border:1px solid #e8eaf0;border-radius:12px;
                            padding:20px;margin-bottom:12px;display:flex;
                            align-items:center;gap:20px;flex-wrap:wrap'>
                    <div style='flex:1;min-width:180px'>
                        <div style='font-size:11px;font-weight:700;color:#6b7280;
                                    text-transform:uppercase;letter-spacing:.06em;
                                    margin-bottom:6px'>Seat</div>
                        <div style='font-size:2rem;font-weight:800;color:#1a1a2e;
                                    letter-spacing:-.02em'>{t.SeatLabel}</div>
                        <div style='display:inline-block;background:{catBg};color:{catColor};
                                    border:1.5px solid {catColor};border-radius:999px;
                                    padding:3px 12px;font-size:11px;font-weight:700;
                                    margin-top:6px'>{t.Category}</div>
                        <div style='margin-top:10px;font-size:11px;color:#6b7280'>
                            Ticket Code
                        </div>
                        <div style='font-family:monospace;font-size:13px;font-weight:700;
                                    color:#1a1a2e;letter-spacing:.04em'>{t.TicketCode}</div>
                        <div style='margin-top:8px;font-size:13px;font-weight:700;
                                    color:#e8344a'>₹{t.Price:F0}</div>
                    </div>
                    <div style='text-align:center;flex-shrink:0'>
                        <img src='cid:{qr.Cid}'
                             alt='QR Code for {t.SeatLabel}'
                             width='130' height='130'
                             style='border-radius:8px;display:block' />
                        <div style='font-size:10px;color:#9ca3af;margin-top:4px'>
                            Scan at gate
                        </div>
                    </div>
                </div>";
            }));

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Booking Confirmed</title>
</head>
<body style='margin:0;padding:0;background:#f3f4f6;font-family:""Inter"",system-ui,sans-serif'>

<div style='max-width:600px;margin:24px auto;background:#ffffff;
            border-radius:16px;overflow:hidden;
            box-shadow:0 4px 24px rgba(0,0,0,.08)'>

    
    <div style='background:#0f0f14;padding:28px 32px;text-align:center'>
        <div style='font-size:1.4rem;font-weight:800;color:#e8344a;
                    letter-spacing:-.02em;margin-bottom:4px'>🎬 CinemaBook</div>
        <div style='color:rgba(255,255,255,.5);font-size:12px'>
            Your tickets are confirmed
        </div>
    </div>

    
    <div style='background:#f0fdf4;border-bottom:2px solid #bbf7d0;
                padding:16px 32px;text-align:center'>
        <div style='font-size:28px;margin-bottom:6px'>✅</div>
        <div style='font-size:1.1rem;font-weight:700;color:#15803d'>
            Booking Confirmed!
        </div>
        <div style='font-size:13px;color:#166534;margin-top:4px'>
            Reference:
            <strong style='font-family:monospace;letter-spacing:.05em'>
                {data.BookingReference}
            </strong>
        </div>
    </div>

    <div style='padding:28px 32px'>

        
        <div style='background:#0f0f14;border-radius:12px;padding:20px;
                    margin-bottom:24px;color:white'>
            <div style='font-size:1.2rem;font-weight:800;margin-bottom:12px;
                        letter-spacing:-.02em'>{data.MovieTitle}</div>
            <div style='display:grid;grid-template-columns:1fr 1fr;
                        gap:10px;font-size:13px'>
                <div>
                    <div style='color:rgba(255,255,255,.5);font-size:11px;
                                text-transform:uppercase;letter-spacing:.06em;
                                margin-bottom:2px'>Date</div>
                    <div style='font-weight:600'>{data.ShowDate:ddd, dd MMM yyyy}</div>
                </div>
                <div>
                    <div style='color:rgba(255,255,255,.5);font-size:11px;
                                text-transform:uppercase;letter-spacing:.06em;
                                margin-bottom:2px'>Time</div>
                    <div style='font-weight:600'>{DateTime.Today.Add(data.StartTime):hh:mm tt}</div>
                </div>
                <div>
                    <div style='color:rgba(255,255,255,.5);font-size:11px;
                                text-transform:uppercase;letter-spacing:.06em;
                                margin-bottom:2px'>Theatre</div>
                    <div style='font-weight:600'>{data.TheatreName}</div>
                </div>
                <div>
                    <div style='color:rgba(255,255,255,.5);font-size:11px;
                                text-transform:uppercase;letter-spacing:.06em;
                                margin-bottom:2px'>Screen</div>
                    <div style='font-weight:600'>{data.ScreenName}</div>
                </div>
                <div>
                    <div style='color:rgba(255,255,255,.5);font-size:11px;
                                text-transform:uppercase;letter-spacing:.06em;
                                margin-bottom:2px'>Language</div>
                    <div style='font-weight:600'>{data.Language}</div>
                </div>
                <div>
                    <div style='color:rgba(255,255,255,.5);font-size:11px;
                                text-transform:uppercase;letter-spacing:.06em;
                                margin-bottom:2px'>City</div>
                    <div style='font-weight:600'>{data.City}</div>
                </div>
            </div>
        </div>

        
        <div style='font-size:.875rem;font-weight:700;color:#1a1a2e;
                    margin-bottom:12px'>
            Your Tickets ({data.Tickets.Count})
        </div>
        {seatsHtml}

        
        <div style='background:#f8f9fc;border:1px solid #e8eaf0;border-radius:12px;
                    padding:16px 20px;margin-top:4px'>
            <div style='display:flex;justify-content:space-between;
                        font-size:13px;color:#6b7280;margin-bottom:6px'>
                <span>Subtotal</span>
                <span>₹{data.SubTotal:F0}</span>
            </div>
            <div style='display:flex;justify-content:space-between;
                        font-size:13px;color:#6b7280;margin-bottom:10px'>
                <span>Convenience fee</span>
                <span>₹{data.ConvenienceFee:F0}</span>
            </div>
            <div style='display:flex;justify-content:space-between;
                        font-size:1rem;font-weight:800;color:#1a1a2e;
                        padding-top:10px;border-top:2px solid #e8eaf0'>
                <span>Total Paid</span>
                <span style='color:#e8344a'>₹{data.TotalAmount:F0}</span>
            </div>
        </div>

        
        <div style='margin-top:24px;padding:16px;background:#fffbeb;
                    border:1px solid #fde68a;border-radius:10px;
                    font-size:13px;color:#92400e'>
            <strong>📱 How to use your ticket:</strong><br>
            Show the QR code above at the theatre gate. Each QR code is valid
            for one entry. Screenshot this email or keep it handy on your phone.
        </div>
    </div>

    
    <div style='background:#f8f9fc;border-top:1px solid #e8eaf0;
                padding:20px 32px;text-align:center'>
        <div style='font-size:12px;color:#9ca3af'>
            This is an automated email from CinemaBook.<br>
            Questions? Contact support at {data.SupportEmail}
        </div>
    </div>
</div>

</body>
</html>";
        }

        // ─────────────────────────────────────────────────────────────────────
        // PLAIN TEXT FALLBACK
        // ─────────────────────────────────────────────────────────────────────
        private static string BuildEmailText(BookingEmailData data)
        {
            var seats = string.Join("\n", data.Tickets.Select(t =>
                $"  Seat {t.SeatLabel} ({t.Category}) — ₹{t.Price:F0} — Code: {t.TicketCode}"));

            return $@"
BOOKING CONFIRMED — CinemaBook
================================
Reference: {data.BookingReference}

MOVIE: {data.MovieTitle}
Date:  {data.ShowDate:ddd, dd MMM yyyy}
Time:  {DateTime.Today.Add(data.StartTime):hh:mm tt}
Venue: {data.TheatreName}, {data.City}
Screen:{data.ScreenName}
Language: {data.Language}

YOUR SEATS:
{seats}

TOTAL PAID: ₹{data.TotalAmount:F0}

Show your ticket code at the gate.
Thank you for booking with CinemaBook!
";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DATA CLASSES
    // ─────────────────────────────────────────────────────────────────────────
    public class EmailSettings
    {
        public string Host { get; set; } = "smtp.gmail.com";
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromName { get; set; } = "CinemaBook";
        public string FromAddress { get; set; } = string.Empty;
        public string SupportEmail { get; set; } = "support@cinemabook.com";
    }

    public class BookingEmailData
    {
        public string BookingReference { get; set; } = string.Empty;
        public string MovieTitle { get; set; } = string.Empty;
        public DateTime ShowDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public string TheatreName { get; set; } = string.Empty;
        public string ScreenName { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public decimal SubTotal { get; set; }
        public decimal ConvenienceFee { get; set; }
        public decimal TotalAmount { get; set; }
        public string SupportEmail { get; set; } = "support@cinemabook.com";
        public List<TicketEmailItem> Tickets { get; set; } = new();
    }
    
    public class TicketEmailItem
    {
        public string TicketCode { get; set; } = string.Empty;
        public string SeatLabel { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }
}
