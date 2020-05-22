using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using System;
using Utiliread.Caching;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RedisCacheExtensions
    {
        public static IServiceCollection AddUtilireadRedisCache(this IServiceCollection services, Action<RedisCacheOptions> setupAction)
        {
            services.AddOptions();
            services.AddSingleton<Utiliread.Caching.StackExchangeRedis.RedisCache>();
            services.AddSingleton((Func<IServiceProvider, ITagableCache>)(sp => sp.GetService<Utiliread.Caching.StackExchangeRedis.RedisCache>()));
            services.AddSingleton((Func<IServiceProvider, IDistributedCache>)(sp => sp.GetService<Utiliread.Caching.StackExchangeRedis.RedisCache>()));

            services.Configure(setupAction);

            return services;
        }
    }
}