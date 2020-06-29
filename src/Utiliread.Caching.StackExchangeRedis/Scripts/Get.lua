local key = KEYS[1]
local now = ARGV[1]
local expire = redis.call('ZSCORE', '_expires-at_', key)
if expire then
    if expire < now then
        return nil
    end
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
end