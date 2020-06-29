# Caching
This library extends the [Microsoft.Extensions.Caching](https://github.com/aspnet/Caching) libraries with the ability to tag and invalidate cache entries.

The main interface for the tagable cache is:
```
    public interface ITagable
    {
        Task TagAsync(string key, string[] tags, CancellationToken cancellationToken = default);
        Task InvalidateAsync(string tag, CancellationToken cancellationToken = default);
        Task InvalidateAsync(string[] tags, CancellationToken cancellationToken = default);
    }
```
The `TagAsync()` is used to associated one or more tags to an already existing entry located at `key`.
Then later, one can invalidate tags with `InvalidateAsync()` and in turn delete _all_ keys that were previously tagged.

The tag and invalidation methods are available as extension methods on `IDistributedCache`.

## Lifetime differences compared to `Microsoft.Extensions.Caching.Distributed.IDistributedCache`
This caching implementation differs from the [Redis implementation](https://github.com/aspnet/Caching/blob/dev/src/Microsoft.Extensions.Caching.Redis/RedisCache.cs) of the `IDistributedCache` in one important way,
namely when _both_ the sliding expiration _and_ the absolute expiration is defined.
The Microsoft implementation allows the sliding expiration to _exceed_ the absolute expiration, where we here treat the absolute expiriation as a finite limit,
so the sliding expiration will never refresh an entry to a later time than configured by the absolute expiration.
