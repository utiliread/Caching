using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;
using Utiliread.Caching.Redis.Scripts;
using RedisCacheOptions = Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions;

namespace Utiliread.Caching.Redis
{
    public class RedisCache : IDistributedCache, ITagable, IDisposable
    {
        private static readonly DateTimeOffset UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly RedisCacheOptions _options;
        private readonly string _prefix;
        private readonly LuaScripts _scripts;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1);

        private volatile ConnectionMultiplexer _connection;

        public RedisCache(IOptions<RedisCacheOptions> optionsAccessor)
        {
            _options = optionsAccessor.Value;

            _prefix = _options.InstanceName ?? string.Empty;
            _scripts = new LuaScripts(_prefix);
        }

        public byte[] Get(string key)
        {
            var connection = GetConnection();
            var database = connection.GetDatabase();

            var now = DateTimeOffset.UtcNow;
            var result = database.ScriptEvaluate(_scripts.Get, new RedisKey[] { _prefix + key }, new RedisValue[] { GetNowUnixMillisecondTimestamp(now) });

            return (byte[])result;
        }

        public async Task<byte[]> GetAsync(string key, CancellationToken token = default)
        {
            var connection = await GetConnectionAsync(token).ConfigureAwait(false);
            var database = connection.GetDatabase();

            var now = DateTimeOffset.UtcNow;
            var result = await database.ScriptEvaluateAsync(_scripts.Get, new RedisKey[] { _prefix + key }, new RedisValue[] { GetNowUnixMillisecondTimestamp(now) });

            return (byte[])result;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            if (options.AbsoluteExpiration != null && options.AbsoluteExpirationRelativeToNow != null)
            {
                throw new ArgumentException();
            }

            var connection = GetConnection();
            var database = connection.GetDatabase();

            var now = DateTimeOffset.UtcNow;
            database.ScriptEvaluate(_scripts.Set, new RedisKey[] { _prefix + key }, new RedisValue[]
            {
                GetNowUnixMillisecondTimestamp(now),
                GetAbsoluteExpirationUnixMillisecondTimestamp(now, options) ?? -1,
                (long?)options.SlidingExpiration?.TotalMilliseconds ?? -1,
                value
            });
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (options.AbsoluteExpiration != null && options.AbsoluteExpirationRelativeToNow != null)
            {
                throw new ArgumentException();
            }

            var connection = await GetConnectionAsync(token).ConfigureAwait(false);
            var database = connection.GetDatabase();

            var now = DateTimeOffset.UtcNow;
            await database.ScriptEvaluateAsync(_scripts.Set, new RedisKey[] { _prefix + key }, new RedisValue[]
            {
                GetNowUnixMillisecondTimestamp(now),
                GetAbsoluteExpirationUnixMillisecondTimestamp(now, options) ?? -1,
                (long?)options.SlidingExpiration?.TotalMilliseconds ?? -1,
                value
            });
        }

        public void Refresh(string key)
        {
            var connection = GetConnection();
            var database = connection.GetDatabase();

            var now = DateTimeOffset.UtcNow;
            database.ScriptEvaluate(_scripts.Refresh, new RedisKey[] { _prefix + key }, new RedisValue[] { GetNowUnixMillisecondTimestamp(now) }, CommandFlags.FireAndForget);
        }

        public async Task RefreshAsync(string key, CancellationToken token = default)
        {
            var connection = await GetConnectionAsync(token).ConfigureAwait(false);
            var database = connection.GetDatabase();

            var now = DateTimeOffset.UtcNow;
            await database.ScriptEvaluateAsync(_scripts.Refresh, new RedisKey[] { _prefix + key }, new RedisValue[] { GetNowUnixMillisecondTimestamp(now) }, CommandFlags.FireAndForget);
        }

        public void Remove(string key)
        {
            var connection = GetConnection();
            var database = connection.GetDatabase();

            database.ScriptEvaluate(_scripts.Remove, new RedisKey[] { _prefix + key });
        }

        public async Task RemoveAsync(string key, CancellationToken token = default)
        {
            var connection = await GetConnectionAsync(token);
            var database = connection.GetDatabase();

            await database.ScriptEvaluateAsync(_scripts.Remove, new RedisKey[] { _prefix + key });
        }

        public async Task TagAsync(string key, string[] tags, CancellationToken token = default)
        {
            var connection = await GetConnectionAsync(token);
            var database = connection.GetDatabase();

            var keys = new RedisKey[tags.Length + 1];

            for (var i = 0; i < tags.Length; i++)
            {
                keys[i] = _prefix + "_tag_:" + tags[i];
            }

            keys[tags.Length] = _prefix + key;

            await database.ScriptEvaluateAsync(_scripts.Tag, keys);
        }

        public async Task InvalidateAsync(string[] tags, CancellationToken token = default)
        {
            var connection = await GetConnectionAsync(token);
            var database = connection.GetDatabase();

            var keys = new RedisKey[tags.Length];

            for (var i = 0; i < tags.Length; i++)
            {
                keys[i] = _prefix + "_tag_:" + tags[i];
            }

            await database.ScriptEvaluateAsync(_scripts.Invalidate, keys, flags: CommandFlags.FireAndForget);
        }

        private ConnectionMultiplexer GetConnection()
        {
            if (_connection != null)
            {
                return _connection;
            }

            _connectionLock.Wait();

            try
            {
                if (_connection != null)
                {
                    return _connection;
                }

                if (_options.ConfigurationOptions is object)
                {
                    _connection = ConnectionMultiplexer.Connect(_options.ConfigurationOptions);
                }
                else
                {
                    _connection = ConnectionMultiplexer.Connect(_options.Configuration);
                }

                return _connection;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task<ConnectionMultiplexer> GetConnectionAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (_connection != null)
            {
                return _connection;
            }

            await _connectionLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                if (_connection != null)
                {
                    return _connection;
                }

                if (_options.ConfigurationOptions is object)
                {
                    _connection = ConnectionMultiplexer.Connect(_options.ConfigurationOptions);
                }
                else
                {
                    _connection = ConnectionMultiplexer.Connect(_options.Configuration);
                }

                return _connection;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private static long GetNowUnixMillisecondTimestamp(DateTimeOffset now) => (long)(now - UnixEpoch).TotalMilliseconds;

        private static long? GetAbsoluteExpirationUnixMillisecondTimestamp(DateTimeOffset now, DistributedCacheEntryOptions options)
        {
            if (options.AbsoluteExpiration.HasValue)
            {
                return (long?)(options.AbsoluteExpiration.Value - UnixEpoch).TotalMilliseconds;
            }
            else if (options.AbsoluteExpirationRelativeToNow.HasValue)
            {
                return (long?)(now + options.AbsoluteExpirationRelativeToNow.Value - UnixEpoch).TotalMilliseconds;
            }

            return null;
        }

        public void Dispose() => _connection?.Close();
    }
}
