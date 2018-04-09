using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using Utiliread.Caching;
using Utiliread.Caching.Redis;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RedisTagableCacheExtensions
    {
        public static IServiceCollection AddDistributedTagabeRedisCache(this IServiceCollection services, Action<RedisTagableCacheOptions> setupAction)
        {
            AddCore(services);

            services.Configure(setupAction);

            return services;
        }

        public static IServiceCollection AddDistributedTagabeRedisCache(this IServiceCollection services, IConfiguration configuration)
        {
            AddCore(services);

            services.Configure<RedisTagableCacheOptions>(configuration);

            return services;
        }

        private static void AddCore(IServiceCollection services)
        {
            services.AddOptions();
            services.AddSingleton<IDistributedTagableCache, RedisTagableCache>();
            services.TryAddSingleton<IDistributedCache>(x => x.GetService<IDistributedTagableCache>());
        }
    }
}