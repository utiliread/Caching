using System;
using System.Threading;
using System.Threading.Tasks;
using Utiliread.Caching;

namespace Microsoft.Extensions.Caching.Distributed
{
    public static class DistributedCacheExtensions
    {
        public static Task TagAsync(this IDistributedCache cache, string key, string[] tags, CancellationToken cancellationToken = default)
        {
            if (cache is ITagableCache tagable)
            {
                return tagable.TagAsync(key, tags, cancellationToken);
            }

            throw new NotSupportedException();
        }

        public static Task InvalidateAsync(this IDistributedCache cache, string tag, CancellationToken cancellationToken = default)
        {
            if (cache is ITagableCache tagable)
            {
                return tagable.InvalidateAsync(tag, cancellationToken);
            }

            return InvalidateAsync(cache, new[] { tag }, cancellationToken);
        }

        public static Task InvalidateAsync(this IDistributedCache cache, string[] tags, CancellationToken cancellationToken = default)
        {
            if (cache is ITagableCache tagable)
            {
                return tagable.InvalidateAsync(tags, cancellationToken);
            }

            throw new NotSupportedException();
        }
    }
}
