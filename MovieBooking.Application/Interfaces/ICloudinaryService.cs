using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace MovieBooking.Application.Interfaces
{
    public interface ICloudinaryService
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <param name="movieTitle"></param>
        /// <returns></returns>
        Task<string?> UploadPosterAsync(IFormFile file, string movieTitle);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="imageUrl"></param>
        /// <returns></returns>
        Task DeletePosterAsync(string imageUrl);

        string GetResizedUrl(string? originalUrl, int width, int height);
    }
}
