using Microsoft.Extensions.Options;

namespace Utiliread.Caching.Redis
{
    public class RedisTagableCacheOptions : IOptions<RedisTagableCacheOptions>
    {
        public string Configuration { get; set; }

        public string InstanceName { get; set; }

        RedisTagableCacheOptions IOptions<RedisTagableCacheOptions>.Value => this;
    }
}
