using System.Threading;
using System.Threading.Tasks;

namespace Utiliread.Caching
{
    public static class DistributedTagableCacheExtensions
    {
        public static Task InvalidateTagAsync(this IDistributedTagableCache cache, string tag, CancellationToken token = default) =>
            cache.InvalidateTagsAsync(new[] { tag }, token);
    }
}
