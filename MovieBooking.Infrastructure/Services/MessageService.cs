using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Twilio;
using Twilio.Jwt.AccessToken;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace MovieBooking.Infrastructure.Services
{
    /// <summary>
    /// message service for conformation
    /// </summary>
    public class MessageService
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<MessageService> logger;

        public MessageService(IConfiguration configuration, ILogger<MessageService> logger)
        {
            this.configuration = configuration;
            this.logger = logger;

            TwilioClient.Init(
                configuration["Twilio:AccountSid"],
                configuration["Twilio:AuthToken"]
                );
        }
        /// <summary>
        /// implementing the Twilio to send message
        /// </summary>
        /// <param name="toPhoneNumber"></param>
        /// <param name="movieTitle"></param>
        /// <param name="bookingReference"></param>
        /// <param name="showDate"></param>
        /// <param name="showTime"></param>
        /// <returns></returns>
        public async Task SendBookingConfirmationAsync(string toPhoneNumber, string movieTitle, string bookingReference, DateTime showDate, TimeSpan showTime)
        {
            var formattedTime = DateTime.Today.Add(showTime).ToString("hh:mm tt");
            try
            {
                var messageBody = $@"
🎬 *Booking Confirmed!*

Movie: {movieTitle}
BookingReference: {bookingReference}

📅 Date: {showDate:dd MMM yyyy}
⏰ Time: {formattedTime}

Show this message at the theatre.

Enjoy your movie 🍿
";
                var message = await MessageResource.CreateAsync(
                    from: new PhoneNumber($"whatsapp:{configuration["Twilio:FromNumber"]}"),
                    to: new PhoneNumber($"whatsapp:{FormatPhoneNumber(toPhoneNumber)}"),
                    body: messageBody
                    );

                logger.LogInformation("Whatsapp sent: {Sid}", message.Sid);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "message failed");
            }

        }

        private string FormatPhoneNumber(string number)
        {
            if (!number.StartsWith("+"))
            {
                return $"+91{number}";
            }
            return number;
        }
    }
}
