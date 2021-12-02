using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using RedisCacheOptions = Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions;

namespace Utiliread.Caching.Redis.Tests.Infrastrcuture
{
    public class RedisFixture : IAsyncLifetime
    {
        private ConnectionMultiplexer _connection;
        private static int _instanceNumber = 0;
        private static readonly ConcurrentDictionary<IDistributedCache, int> _index = new();
        private static readonly ConcurrentDictionary<IDistributedCache, IServiceProvider> _services = new();

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

        public IDatabase Database { get; private set; }

        public async Task InitializeAsync()
        {
            _connection = await ConnectionMultiplexer.ConnectAsync("localhost");
            Database = _connection.GetDatabase();

            await Database.ScriptEvaluateAsync(CleanupScript);
        }

        public IDistributedCache CreateCacheInstance()
        {
            var instanceNumber = Interlocked.Increment(ref _instanceNumber);

            var services = new ServiceCollection()
                .AddUtilireadRedisCache(options =>
                {
                    options.Configuration = "localhost";
                    options.InstanceName = $"TagableCacheTestFixture:{instanceNumber}:";
                })
                .BuildServiceProvider();

            var instance = services.GetRequiredService<IDistributedCache>();

            _index[instance] = instanceNumber;
            _services[instance] = services;

            return instance;
        }

        public string GetInstanceName(IDistributedCache cache)
        {
            var instanceNumber = _index[cache];
            return $"TagableCacheTestFixture:{instanceNumber}:";
        }

        public Task<string[]> GetKeysAsync(IDistributedCache cache)
        {
            var instanceNumber = _index[cache];

            var server = _connection.GetServer(_connection.GetEndPoints().First());

            var keys = server.Keys(pattern: $"TagableCacheTestFixture:{instanceNumber}:*");

            return Task.FromResult(keys.ToArray().Select(x => (string)x).ToArray());
        }

        public async Task RunExpireAsync(IDistributedCache cache)
        {
            var expirer = _services[cache].GetRequiredService<RedisExpirer>();

            await expirer.EnsureConnectionAsync();
            await expirer.RunExpireAsync();
        }

        public async Task DisposeAsync()
        {
            await Database.ScriptEvaluateAsync(CleanupScript);

            _connection.Dispose();
        }
    }
}
