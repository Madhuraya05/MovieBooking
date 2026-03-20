using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MovieBooking.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace MovieBooking.Infrastructure.Services
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;
        private readonly ILogger<CloudinaryService> logger;

        public CloudinaryService(IConfiguration configuration,ILogger<CloudinaryService> logger)
        {
            var cloudName = configuration["Cloudinary:CloudName"]
                 ?? throw new Exception("Cloudinary:CloudName are not found in appsetting.json");
            var apiKey = configuration["Cloudinary:ApiKey"]
              ?? throw new Exception("Cloudinary:ApiKey not found in appsettings.json");
            var apiSecret = configuration["Cloudinary:ApiSecret"]
                ?? throw new Exception("Cloudinary:ApiSecret not found in appsettings.json");

            var account = new Account(cloudName, apiKey, apiSecret);
            _cloudinary = new Cloudinary(account);
            _cloudinary.Api.Secure = true;
            this.logger = logger;
        }
        public async Task DeletePosterAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl)) return;

            try
            {
                var uri = new Uri(imageUrl);
                var segments = uri.AbsolutePath.Split('/');
                var uploadIndex = Array.IndexOf(segments, "upload");

                if (uploadIndex < 0) return;

                var publicIdParts = segments
                    .Skip(uploadIndex + 1)
                    .Where(s => !s.StartsWith("v") || !s.Skip(1).All(char.IsDigit))
                    .ToArray();

                var publicIdWithExt = string.Join("/", publicIdParts);

                var publicId = System.IO.Path.GetFileNameWithoutExtension(publicIdWithExt);

                var fullPublicId = string.Join("/", publicIdParts.Take(publicIdParts.Length - 1)
                    .Append(publicId));

                var deleteParams = new DeletionParams(fullPublicId);

                await _cloudinary.DestroyAsync(deleteParams);
            }
            catch
            {

            }
        }

        public string GetResizedUrl(string? originalUrl, int width, int height)
        {
            if (string.IsNullOrEmpty(originalUrl))
                return "/images/no-poster.jpg";

            logger.LogInformation($"the resizedurl {originalUrl.Replace(
                "/upload/",
                $"/upload/w_{width},h_{height},c_fill,g_auto/"
                )}");

            return originalUrl.Replace(
                "/upload/",
                $"/upload/w_{width},h_{height},c_fill,g_auto/"
                );
        }

        public async Task<string?> UploadPosterAsync(IFormFile file, string movieTitle)
        {
            var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(file.ContentType.ToLower()))
                throw new InvalidOperationException("OnlyJPEG, PNG and WebP images are allowed.");

            if (file.Length >5 * 1024 * 1024)
                throw new InvalidOperationException("Poster image must be under 5MB.");

            var safeTitle = movieTitle
                .ToLower()
                .Replace(" ", "-")
                .Replace("'", "")
                .Replace(":", "");

            safeTitle = safeTitle.Substring(0, Math.Min(safeTitle.Length, 40));

            var publicId = $"movie-posters/{safeTitle}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                PublicId = publicId ,

                EagerTransforms = new List<Transformation>
                {
                    new Transformation().Width(300).Height(450).Crop("fill").Gravity("auto"),
                    new Transformation().Width(150).Height(225).Crop("fill").Gravity("auto")
                },
                EagerAsync = true,

                Overwrite = true,

                Folder = "cinema-booking"
            };

            var result = await _cloudinary.UploadAsync(uploadParams);
            logger.LogInformation($"The result url after upload {result.SecureUrl.ToString()}");
            return result.SecureUrl.ToString();
            
        }
    }
}
