local key = KEYS[1]
local count = redis.call('DEL', key)
if count == 1 then
    local keyTags = redis.call('SMEMBERS', key..':_tags_')
    for _, keyTag in pairs(keyTags) do
        redis.call('SREM', keyTag, key)
 
     end
    redis.call('DEL', key..':_tags_')
    redis.call('ZREM', '_expires-at_', key)
end
return count