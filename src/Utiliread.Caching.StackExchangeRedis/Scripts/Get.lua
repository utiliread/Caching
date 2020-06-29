﻿local key = KEYS[1]
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
end