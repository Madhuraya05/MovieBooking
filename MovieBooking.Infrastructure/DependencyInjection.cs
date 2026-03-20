using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieBooking.Application.Interfaces;
using MovieBooking.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace MovieBooking.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services,IConfiguration configuration)
        {
            var currentAssembly = typeof(DependencyInjection).Assembly;

            //services.AddScoped<ICloudinaryService, CloudinaryService>();
            services.AddScoped<CloudinaryService> ();
            services.AddHostedService<SeatExpiryService>();
            return services;
        }
    }
}
