using System;
using System.Threading;
using System.Threading.Tasks;
using Utiliread.Caching;

namespace Microsoft.Extensions.Caching.Distributed
{
    public static class TagableExtensions
    {
        public static Task TagAsync(this IDistributedCache cache, string key, string[] tags, CancellationToken cancellationToken = default)
        {
            return GetTagable(cache).TagAsync(key, tags, cancellationToken);
        }

        public static Task InvalidateAsync(this IDistributedCache cache, string tag, CancellationToken cancellationToken = default)
        {
            return GetTagable(cache).InvalidateAsync(tag, cancellationToken);
        }

        public static Task InvalidateAsync(this IDistributedCache cache, string[] tags, CancellationToken cancellationToken = default)
        {
            return GetTagable(cache).InvalidateAsync(tags, cancellationToken);
        }

        private static ITagable GetTagable(IDistributedCache cache)
        {
            if (cache is ITagable tagable)
            {
                return tagable;
            }

            throw new NotSupportedException("The cache does not implement ITagable");
        }
    }
}
