using MovieBooking.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace MovieBooking.Infrastructure.Services.Interfaces
{
    public interface IJwtService
    {
        Task<string> GenerateToken(AppUser user);

        string GenerateRefreshToken();
        string HashToken(string token);
    }
}
