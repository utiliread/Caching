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
return 1