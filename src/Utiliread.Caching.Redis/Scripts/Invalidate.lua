local count = 0
for _, tag in pairs(KEYS) do
    local expiredKeys = redis.call('SMEMBERS', tag)
    if #expiredKeys > 0 then
        redis.call('DEL', unpack(expiredKeys))
        for _, key in pairs(expiredKeys) do
            local keyTags = redis.call('SMEMBERS', key..':_tags_')
            for _, keyTag in pairs(keyTags) do
        redis.call('SREM', keyTag, key)
 
             end
            redis.call('DEL', key..':_tags_')
        end
        redis.call('ZREM', '_expires-at_', unpack(expiredKeys))
        count = count + #expiredKeys
    end
end
return count