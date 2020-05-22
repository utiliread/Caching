using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Utiliread.Caching.StackExchangeRedis;
using Xunit;

namespace Utiliread.Caching.Redis.Tests.Infrastrcuture
{
    public class RedisTestFixture : IAsyncLifetime
    {
        private ConnectionMultiplexer _connection;
        private IDatabase _cache;
        private static int _instanceNumber = 0;
        private static ConcurrentDictionary<RedisCache, int> _instances = new ConcurrentDictionary<RedisCache, int>();

        private const string CleanupScript = @"
local keys = redis.call('KEYS', 'TagableCacheTestFixture:*')
if #keys > 0 then
    local i = 1
    while i <= #keys-1000 do
        redis.call('DEL', unpack(keys, i, i + 999))
        i = i + 1000
    end
    redis.call('DEL', unpack(keys, i, #keys))
end
return 0";

        public async Task InitializeAsync()
        {
            _connection = await ConnectionMultiplexer.ConnectAsync("localhost");
            _cache = _connection.GetDatabase();

            await _cache.ScriptEvaluateAsync(CleanupScript);
        }

        public RedisCache CreateCacheInstance()
        {
            var instanceNumber = Interlocked.Increment(ref _instanceNumber);

            var instance = new RedisCache(new Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions()
            {
                Configuration = "localhost",
                InstanceName = $"TagableCacheTestFixture:{instanceNumber}"
            });

            _instances[instance] = instanceNumber;

            return instance;
        }

        public Task<string[]> GetKeysAsync(RedisCache cache)
        {
            var instanceNumber = _instances[cache];

            var server = _connection.GetServer(_connection.GetEndPoints().First());

            var keys = server.Keys(pattern: $"TagableCacheTestFixture:{instanceNumber}:*");

            return Task.FromResult(keys.ToArray().Select(x => (string)x).ToArray());
        }

        public async Task DisposeAsync()
        {
            await _cache.ScriptEvaluateAsync(CleanupScript);

            _connection.Dispose();
        }
    }
}
