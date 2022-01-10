using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Utiliread.Caching.Redis.Tests.Infrastrcuture
{
    public class RedisFixture : IAsyncLifetime
    {
        private static int _fixtureCount = 0;
        private readonly int _fixtureNumber;
        private int _cacheCount = 0;
        private ConnectionMultiplexer _connection;
        private readonly Dictionary<IDistributedCache, (int CacheNumber, IServiceProvider Services)> _caches = new();

        private const string CleanupScript = @"
local keys = redis.call('KEYS', 'TagableCacheTestFixture:fixture-{{fixtureNumber}}:*')
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

        public RedisFixture()
        {
            _fixtureNumber = Interlocked.Increment(ref _fixtureCount);
        }

        public async Task InitializeAsync()
        {
            _connection = await ConnectionMultiplexer.ConnectAsync("localhost");
            Database = _connection.GetDatabase();

            await RunCleanupAsync();
        }

        public IDistributedCache CreateCacheInstance()
        {
            var instanceNumber = ++_cacheCount;
            var services = new ServiceCollection()
                .AddUtilireadRedisCache(options =>
                {
                    options.Configuration = "localhost";
                    options.InstanceName = $"TagableCacheTestFixture:fixture-{_fixtureNumber}:{instanceNumber}:";
                })
                .BuildServiceProvider();

            var instance = services.GetRequiredService<IDistributedCache>();
            _caches[instance] = (instanceNumber, services);
            return instance;
        }

        public string GetInstanceName(IDistributedCache cache)
        {
            var instanceNumber = _caches[cache].CacheNumber;
            return $"TagableCacheTestFixture:fixture-{_fixtureNumber}:{instanceNumber}:";
        }

        public Task<string[]> GetKeysAsync(IDistributedCache cache)
        {
            var instanceNumber = _caches[cache].CacheNumber;
            var server = _connection.GetServer(_connection.GetEndPoints().First());

            var keys = server.Keys(pattern: $"TagableCacheTestFixture:fixture-{_fixtureNumber}:{instanceNumber}:*");

            return Task.FromResult(keys.ToArray().Select(x => (string)x).ToArray());
        }

        public async Task RunExpireAsync(IDistributedCache cache)
        {
            var expirer = _caches[cache].Services.GetRequiredService<RedisExpirer>();
            await expirer.EnsureConnectionAsync();
            await expirer.RunExpireAsync();
        }

        public async Task DisposeAsync()
        {
            await RunCleanupAsync();

            _connection.Dispose();
        }

        private Task RunCleanupAsync()
        {
            return Database.ScriptEvaluateAsync(CleanupScript.Replace("{{fixtureNumber}}", _fixtureNumber.ToString()));
        }
    }
}
