using Microsoft.Extensions.Caching.Distributed;
using System;
using Utiliread.Caching.StackExchangeRedis;
using RedisCacheOptions = Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RedisCacheExtensions
    {
        public static IServiceCollection AddUtilireadRedisCache(this IServiceCollection services, Action<RedisCacheOptions> setupAction)
        {
            services.AddOptions();
            services.AddSingleton<IDistributedCache, RedisCache>();
            services.Configure(setupAction);

            return services;
        }
    }
}