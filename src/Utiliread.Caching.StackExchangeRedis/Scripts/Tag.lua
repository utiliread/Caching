local key = KEYS[#KEYS]
local exists = redis.call('EXISTS', key)
table.remove(KEYS, #KEYS)
if exists == 1 and #KEYS > 0 then
    for _,tag in pairs(KEYS) do
        redis.call('SADD', tag, key)
    end
    -- We need to process the keys in batches as unpack() can return "too many results to unpack",
    -- see https://github.com/antirez/redis/issues/678#issuecomment-15848571
    local i = 1
    while i <= #KEYS-1000 do
        redis.call('SADD', key .. ':_tags_', unpack(KEYS, i, i + 999))
        i = i + 1000
    end
    redis.call('SADD', key .. ':_tags_', unpack(KEYS, i, #KEYS))
    return 1
end
return 0