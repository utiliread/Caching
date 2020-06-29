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
return 0