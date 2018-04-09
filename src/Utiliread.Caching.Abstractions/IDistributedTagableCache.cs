using Microsoft.Extensions.Caching.Distributed;
using System.Threading;
using System.Threading.Tasks;

namespace Utiliread.Caching
{
    public interface IDistributedTagableCache : IDistributedCache
    {
        Task TagAsync(string key, string[] tags, CancellationToken token = default);

        Task InvalidateTagsAsync(string[] tags, CancellationToken token = default);
    }
}