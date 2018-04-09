namespace Microsoft.Extensions.Caching.Distributed
{
    public static class DistributedCacheEntryOptionsExtensions
    {
        public static DistributedCacheEntryOptions SetNeverExpires(this DistributedCacheEntryOptions options)
        {
            options.AbsoluteExpiration = null;
            options.AbsoluteExpirationRelativeToNow = null;
            options.SlidingExpiration = null;

            return options;
        }
    }
}
