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
            var result = database.ScriptEvaluate(_scripts.Get, new RedisKey[] { _prefix + key }, new RedisValue[] { now.ToUnixTimeMilliseconds() });
            return (byte[])result;
        }

        public async Task<byte[]> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var database = connection.GetDatabase();
            var now = DateTimeOffset.UtcNow;
            var result = await database
                .ScriptEvaluateAsync(_scripts.Get, new RedisKey[] { _prefix + key }, new RedisValue[] { now.ToUnixTimeMilliseconds() })
                .ConfigureAwait(false);
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
                now.ToUnixTimeMilliseconds(),
                GetAbsoluteExpirationUnixMillisecondTimestamp(now, options) ?? -1,
                (long?)options.SlidingExpiration?.TotalMilliseconds ?? -1,
                value
            });
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken cancellationToken = default)
        {
            if (options.AbsoluteExpiration != null && options.AbsoluteExpirationRelativeToNow != null)
            {
                throw new ArgumentException();
            }

            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var database = connection.GetDatabase();
            var now = DateTimeOffset.UtcNow;
            await database
                .ScriptEvaluateAsync(_scripts.Set, new RedisKey[] { _prefix + key }, new RedisValue[]
                {
                    now.ToUnixTimeMilliseconds(),
                    GetAbsoluteExpirationUnixMillisecondTimestamp(now, options) ?? -1,
                    (long?)options.SlidingExpiration?.TotalMilliseconds ?? -1,
                    value
                })
                .ConfigureAwait(false);
        }

        public void Refresh(string key)
        {
            var connection = GetConnection();
            var database = connection.GetDatabase();
            var now = DateTimeOffset.UtcNow;
            database.ScriptEvaluate(_scripts.Refresh, new RedisKey[] { _prefix + key }, new RedisValue[] { now.ToUnixTimeMilliseconds() }, CommandFlags.FireAndForget);
        }

        public async Task RefreshAsync(string key, CancellationToken cancellationToken = default)
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var database = connection.GetDatabase();
            var now = DateTimeOffset.UtcNow;
            await database
                .ScriptEvaluateAsync(_scripts.Refresh, new RedisKey[] { _prefix + key }, new RedisValue[] { now.ToUnixTimeMilliseconds() }, CommandFlags.FireAndForget)
                .ConfigureAwait(false);
        }

        public void Remove(string key)
        {
            var connection = GetConnection();
            var database = connection.GetDatabase();
            database.ScriptEvaluate(_scripts.Remove, new RedisKey[] { _prefix + key });
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var database = connection.GetDatabase();
            await database
                .ScriptEvaluateAsync(_scripts.Remove, new RedisKey[] { _prefix + key })
                .ConfigureAwait(false);
        }

        public async Task TagAsync(string key, string[] tags, CancellationToken cancellationToken = default)
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var database = connection.GetDatabase();

            var keys = new RedisKey[tags.Length + 1];
            for (var i = 0; i < tags.Length; i++)
            {
                keys[i] = _prefix + "_tag_:" + tags[i];
            }
            keys[tags.Length] = _prefix + key;

            await database
                .ScriptEvaluateAsync(_scripts.Tag, keys)
                .ConfigureAwait(false);
        }

        public async Task InvalidateAsync(string[] tags, CancellationToken cancellationToken = default)
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var database = connection.GetDatabase();

            var keys = new RedisKey[tags.Length];
            for (var i = 0; i < tags.Length; i++)
            {
                keys[i] = _prefix + "_tag_:" + tags[i];
            }

            await database
                .ScriptEvaluateAsync(_scripts.Invalidate, keys, flags: CommandFlags.FireAndForget)
                .ConfigureAwait(false);
        }

        private ConnectionMultiplexer GetConnection()
        {
            if (_connection is not null)
            {
                return _connection;
            }

            _connectionLock.Wait();

            try
            {
                return _connection ??= Connect();
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private ConnectionMultiplexer Connect()
        {
            if (_options.ConfigurationOptions is not null)
            {
                return ConnectionMultiplexer.Connect(_options.ConfigurationOptions);
            }
            else
            {
                return ConnectionMultiplexer.Connect(_options.Configuration);
            }
        }

        private async Task<ConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_connection is not null)
            {
                return _connection;
            }

            await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return _connection ??= await ConnectAsync().ConfigureAwait(false);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private Task<ConnectionMultiplexer> ConnectAsync()
        {
            if (_options.ConfigurationOptions is not null)
            {
                return ConnectionMultiplexer.ConnectAsync(_options.ConfigurationOptions);
            }
            else
            {
                return ConnectionMultiplexer.ConnectAsync(_options.Configuration);
            }
        }

        private static long? GetAbsoluteExpirationUnixMillisecondTimestamp(DateTimeOffset now, DistributedCacheEntryOptions options)
        {
            if (options.AbsoluteExpiration.HasValue)
            {
                return options.AbsoluteExpiration?.ToUnixTimeMilliseconds();
            }
            else if (options.AbsoluteExpirationRelativeToNow.HasValue)
            {
                return (now + options.AbsoluteExpirationRelativeToNow.Value).ToUnixTimeMilliseconds();
            }

            return null;
        }

        public void Dispose() => _connection?.Close();
    }
}
