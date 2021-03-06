using Microsoft.Extensions.Caching.Distributed;
using System;
using Utiliread.Caching.Redis;
using RedisCacheOptions = Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RedisCacheExtensions
    {
        public static IServiceCollection AddUtilireadRedisCache(this IServiceCollection services, Action<RedisCacheOptions> setupAction)
        {
            services.AddOptions();
            services.AddSingleton<IDistributedCache, RedisCache>();
            services.AddSingleton<RedisExpirer>();
            services.AddHostedService(sp => sp.GetService<RedisExpirer>());
            services.Configure(setupAction);

            return services;
        }
    }
}