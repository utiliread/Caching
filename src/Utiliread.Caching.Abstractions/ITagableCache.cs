using System.Threading;
using System.Threading.Tasks;

namespace Utiliread.Caching
{
    public interface ITagableCache
    {
        Task TagAsync(string key, string[] tags, CancellationToken cancellationToken = default);

        Task InvalidateAsync(string[] tags, CancellationToken cancellationToken = default);

        Task InvalidateAsync(string tag, CancellationToken cancellationToken = default)
            => InvalidateAsync(new[] { tag }, cancellationToken);
    }
}