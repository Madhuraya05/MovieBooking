using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace MovieBooking.Application.Interfaces
{
    public interface ICloudinaryService
    {
        Task<string?> UploadPosterAsync(IFormFile file, string movieTitle);

        Task DeletePosterAsync(string imageUrl);

        string GetResizedUrl(string? originalUrl, int width, int height);
    }
}
