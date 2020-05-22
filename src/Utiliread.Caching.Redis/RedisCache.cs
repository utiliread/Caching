using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Utiliread.Caching.Redis
{
    public class RedisCache : IDistributedCache, ITagableCache, IDisposable
    {
        private static readonly DateTimeOffset UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly RedisCacheOptions _options;
        private readonly string _prefix = string.Empty;
        private readonly string _getScript;
        private readonly string _setScript;
        private readonly string _refreshScript;
        private readonly string _removeScript;
        private readonly string _tagScript;
        private readonly string _invalidateScript;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        private volatile ConnectionMultiplexer _connection;
        private IDatabase _cache;

        // KEYS[1] The key
        // ARGV[1] Now unix milliseconds timestamp
        private const string GetScript = @"
local key = KEYS[1]
local now = ARGV[1]
local expiredKeys = redis.call('ZRANGEBYSCORE', '_expires-at_', '-inf', now)
if #expiredKeys > 0 then
    local i = 1
    while i <= #expiredKeys-1000 do
        redis.call('DEL', unpack(expiredKeys, i, i + 999))
        i = i + 1000
    end
    redis.call('DEL', unpack(expiredKeys, i, #expiredKeys))
    for _,key in pairs(expiredKeys) do
        local keyTags = redis.call('SMEMBERS', key .. ':_tags_')
        for _,keyTag in pairs(keyTags) do
            redis.call('SREM', keyTag, key)
        end
        redis.call('DEL', key .. ':_tags_')
    end
    redis.call('ZREMRANGEBYSCORE', '_expires-at_', '-inf', now)
end
if redis.call('ZSCORE', '_expires-at_', key) then
    local values = redis.call('HMGET', key, 'absexp', 'sldexp', 'data')
    if values[1] ~= nil and values[2] ~= '-1' then
        if values[1] ~= '-1' then
            redis.call('ZADD', '_expires-at_', math.min(0 + values[1], now + values[2]), key)
        else
            redis.call('ZADD', '_expires-at_', now + values[2], key)
        end
    end
    return values[3]
else
    return redis.call('HGET', key, 'data')
end";

        // KEYS[1] The key
        // ARGV[1] Now unix milliseconds timestamp
        // ARGV[2] Absolute expiration unix timestamp in milliseconds, -1 if none
        // ARGV[3] Sliding expiration in milliseconds, -1 if none
        // ARGV[4] The data
        private const string SetScript = @"
local key = KEYS[1]
local now = ARGV[1]
redis.call('HMSET', key, 'crtd', ARGV[1], 'absexp', ARGV[2], 'sldexp', ARGV[3], 'data', ARGV[4])
if ARGV[2] ~= '-1' and ARGV[3] ~= '-1' then
    redis.call('ZADD', '_expires-at_', math.min(0 + ARGV[2], now + ARGV[3]), key)
elseif ARGV[2] ~= '-1' then
    redis.call('ZADD', '_expires-at_', ARGV[2], key)
elseif ARGV[3] ~= '-1' then
    redis.call('ZADD', '_expires-at_', now + ARGV[3], key)
else
    redis.call('ZREM', '_expires-at_', key)
end
return 1";

        // KEYS[1] The key
        // ARGV[1] Now unix milliseconds timestamp
        private const string RefreshScript = @"
local key = KEYS[1]
local now = ARGV[1]
local exp = redis.call('HMGET', key, 'absexp', 'sldexp')
if exp[1] ~= nil then
    if exp[1] ~= '-1' and exp[2] ~= '-1' then
        redis.call('ZADD', '_expires-at_', math.min(0 + exp[1], now + exp[2]), key)
        return 2
    elseif exp[1] ~= '-1' then
        redis.call('ZADD', '_expires-at_', exp[1], key)
        return 2
    elseif exp[2] ~= '-1' then
        redis.call('ZADD', '_expires-at_', now + exp[2], key)
        return 2
    else
        return 1
    end
end
return 0";

        // KEYS[1] The key
        private const string RemoveScript = @"
local key = KEYS[1]
local count = redis.call('DEL', key)
if count == 1 then
    local keyTags = redis.call('SMEMBERS', key .. ':_tags_')
    for _,keyTag in pairs(keyTags) do
        redis.call('SREM', keyTag, key)
    end
    redis.call('DEL', key .. ':_tags_')
    redis.call('ZREM', '_expires-at_', key)
end
return count";

        // KEYS[1..N-1] The tags to add
        // KEYS[N]      The key to tag
        // We need to process the keys in batches as unpack() can return "too many results to unpack",
        // see https://github.com/antirez/redis/issues/678#issuecomment-15848571
        private const string TagScript = @"
local key = KEYS[#KEYS]
local exists = redis.call('EXISTS', key)
table.remove(KEYS, #KEYS)
if exists == 1 and #KEYS > 0 then
    for _,tag in pairs(KEYS) do
        redis.call('SADD', tag, key)
    end
    local i = 1
    while i <= #KEYS-1000 do
        redis.call('SADD', key .. ':_tags_', unpack(KEYS, i, i + 999))
        i = i + 1000
    end
    redis.call('SADD', key .. ':_tags_', unpack(KEYS, i, #KEYS))
    return 1
end
return 0";

        // KEYS[1..N] The tags to invalidate
        private const string InvalidateScript = @"
local count = 0
for _,tag in pairs(KEYS) do
    local expiredKeys = redis.call('SMEMBERS', tag)
    if #expiredKeys > 0 then
        redis.call('DEL', unpack(expiredKeys))
        for _,key in pairs(expiredKeys) do
            local keyTags = redis.call('SMEMBERS', key .. ':_tags_')
            for _,keyTag in pairs(keyTags) do
                redis.call('SREM', keyTag, key)
            end
            redis.call('DEL', key .. ':_tags_')
        end
        redis.call('ZREM', '_expires-at_', unpack(expiredKeys))
        count = count + #expiredKeys
    end
end
return count";

        public RedisCache(IOptions<RedisCacheOptions> optionsAccessor)
        {
            _options = optionsAccessor.Value;

            if (!string.IsNullOrEmpty(_options.InstanceName))
            {
                _prefix = $"{_options.InstanceName}:";
                _getScript = GetScript.Replace("_expires-at_", $"{_prefix}_expires-at_");
                _setScript = SetScript.Replace("_expires-at_", $"{_prefix}_expires-at_");
                _refreshScript = RefreshScript.Replace("_expires-at_", $"{_prefix}_expires-at_");
                _removeScript = RemoveScript.Replace("_expires-at_", $"{_prefix}_expires-at_");
                _tagScript = TagScript.Replace("_expires-at_", $"{_prefix}_expires-at_");
                _invalidateScript = InvalidateScript.Replace("_expires-at_", $"{_prefix}_expires-at_");
            }
        }

        public byte[] Get(string key)
        {
            Connect();

            var now = DateTimeOffset.UtcNow;
            var result = _cache.ScriptEvaluate(_getScript, new RedisKey[] { _prefix + key }, new RedisValue[] { GetNowUnixMillisecondTimestamp(now) });

            return (byte[])result;
        }

        public async Task<byte[]> GetAsync(string key, CancellationToken token = default)
        {
            await ConnectAsync(token);

            var now = DateTimeOffset.UtcNow;
            var result = await _cache.ScriptEvaluateAsync(_getScript, new RedisKey[] { _prefix + key }, new RedisValue[] { GetNowUnixMillisecondTimestamp(now) });

            return (byte[])result;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            if (options.AbsoluteExpiration != null && options.AbsoluteExpirationRelativeToNow != null)
            {
                throw new ArgumentException();
            }

            Connect();

            var now = DateTimeOffset.UtcNow;
            _cache.ScriptEvaluate(_setScript, new RedisKey[] { _prefix + key }, new RedisValue[]
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

            await ConnectAsync(token);

            var now = DateTimeOffset.UtcNow;
            await _cache.ScriptEvaluateAsync(_setScript, new RedisKey[] { _prefix + key }, new RedisValue[]
            {
                GetNowUnixMillisecondTimestamp(now),
                GetAbsoluteExpirationUnixMillisecondTimestamp(now, options) ?? -1,
                (long?)options.SlidingExpiration?.TotalMilliseconds ?? -1,
                value
            });
        }

        public void Refresh(string key)
        {
            Connect();

            var now = DateTimeOffset.UtcNow;
            _cache.ScriptEvaluate(_refreshScript, new RedisKey[] { _prefix + key }, new RedisValue[] { GetNowUnixMillisecondTimestamp(now) }, CommandFlags.FireAndForget);
        }

        public async Task RefreshAsync(string key, CancellationToken token = default)
        {
            await ConnectAsync(token);

            var now = DateTimeOffset.UtcNow;
            await _cache.ScriptEvaluateAsync(_refreshScript, new RedisKey[] { _prefix + key }, new RedisValue[] { GetNowUnixMillisecondTimestamp(now) }, CommandFlags.FireAndForget);
        }

        public void Remove(string key)
        {
            Connect();

            _cache.ScriptEvaluate(_removeScript, new RedisKey[] { _prefix + key });
        }

        public async Task RemoveAsync(string key, CancellationToken token = default)
        {
            await ConnectAsync(token);

            await _cache.ScriptEvaluateAsync(_removeScript, new RedisKey[] { _prefix + key });
        }

        public async Task TagAsync(string key, string[] tags, CancellationToken token = default)
        {
            await ConnectAsync(token);

            var keys = new RedisKey[tags.Length + 1];

            for (var i = 0; i < tags.Length; i++)
            {
                keys[i] = _prefix + "_tag_:" + tags[i];
            }

            keys[tags.Length] = _prefix + key;

            await _cache.ScriptEvaluateAsync(_tagScript, keys);
        }

        public async Task InvalidateAsync(string[] tags, CancellationToken token = default)
        {
            await ConnectAsync(token);

            var keys = new RedisKey[tags.Length];

            for (var i = 0; i < tags.Length; i++)
            {
                keys[i] = _prefix + "_tag_:" + tags[i];
            }

            await _cache.ScriptEvaluateAsync(_invalidateScript, keys, flags: CommandFlags.FireAndForget);
        }

        private void Connect()
        {
            if (_connection != null)
            {
                return;
            }

            _connectionLock.Wait();

            try
            {
                if (_connection == null)
                {
                    _connection = ConnectionMultiplexer.Connect(_options.Configuration);
                    _cache = _connection.GetDatabase();
                }
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task ConnectAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (_connection != null)
            {
                return;
            }

            await _connectionLock.WaitAsync();

            try
            {
                if (_connection == null)
                {
                    _connection = await ConnectionMultiplexer.ConnectAsync(_options.Configuration);
                    _cache = _connection.GetDatabase();
                }
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
