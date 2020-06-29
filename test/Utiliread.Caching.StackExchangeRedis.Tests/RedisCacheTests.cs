using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Linq;
using System.Threading.Tasks;
using Utiliread.Caching.Redis.Tests.Infrastrcuture;
using Xunit;

namespace Utiliread.Caching.Redis.Tests
{
    public class RedisCacheTests : IClassFixture<RedisFixture>
    {
        private readonly RedisFixture _fixture;
        private readonly IDistributedCache _cache;

        public RedisCacheTests(RedisFixture fixture)
        {
            _fixture = fixture;
            _cache = _fixture.CreateCacheInstance();
        }

        [Fact]
        public async Task Get()
        {
            // Given
            await _cache.SetStringAsync("key", "value");

            // When
            Assert.Equal("value", await _cache.GetStringAsync("key"));

            // Then
        }

        [Fact]
        public async Task Get_KeyDoesNotExist()
        {
            // Given

            // When
            Assert.Null(await _cache.GetAsync("key"));

            // Then
        }

        [Fact]
        public async Task Set_AlwaysOverrides()
        {
            // Given
            await _cache.SetStringAsync("key", "old value");

            // When
            await _cache.SetStringAsync("key", "new value");

            // Then
            Assert.Equal("new value", await _cache.GetStringAsync("key"));
        }

        [Fact]
        public async Task Set_NullValueThrows()
        {
            // Given

            // When
            await Assert.ThrowsAsync<ArgumentNullException>(() => _cache.SetStringAsync("key", null));

            // Then
        }

        [Fact]
        public async Task Set_SlidingExpiration()
        {
            // Given
            var options = new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMilliseconds(100));

            await _cache.SetStringAsync("key", "value", options);

            // When
            Assert.Equal("value", await _cache.GetStringAsync("key"));
            await Task.Delay(50);
            Assert.Equal("value", await _cache.GetStringAsync("key"));
            await Task.Delay(50);
            Assert.Equal("value", await _cache.GetStringAsync("key"));
            await Task.Delay(50);
            Assert.Equal("value", await _cache.GetStringAsync("key"));
            await Task.Delay(50);
            Assert.Equal("value", await _cache.GetStringAsync("key"));

            // Then
            await Task.Delay(150);
            Assert.Null(await _cache.GetStringAsync("key"));
        }

        [Fact]
        public async Task Set_RelativeExpiration()
        {
            // Given
            var options = new DistributedCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMilliseconds(100));

            await _cache.SetStringAsync("key", "value", options);

            // When
            Assert.Equal("value", await _cache.GetStringAsync("key"));
            await Task.Delay(50);
            Assert.Equal("value", await _cache.GetStringAsync("key"));

            // Then
            await Task.Delay(100);
            Assert.Null(await _cache.GetStringAsync("key"));
        }

        [Fact]
        public async Task Set_AbsoluteExpiration()
        {
            // Given
            var now = DateTime.UtcNow;
            var options = new DistributedCacheEntryOptions().SetAbsoluteExpiration(now.AddMilliseconds(100));

            await _cache.SetStringAsync("key", "value", options);

            // When
            Assert.Equal("value", await _cache.GetStringAsync("key"));
            await Task.Delay(50);
            Assert.Equal("value", await _cache.GetStringAsync("key"));

            // Then
            await Task.Delay(100);
            Assert.Null(await _cache.GetStringAsync("key"));
        }

        [Fact]
        public async Task Set_SlidingExpirationShouldExpireEventWithAbsoluteExpiration()
        {
            // Given
            var now = DateTime.UtcNow;
            var options = new DistributedCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMilliseconds(50))
                .SetAbsoluteExpiration(now.AddMilliseconds(200));

            await _cache.SetStringAsync("key", "value", options);

            // When
            Assert.NotNull(await _cache.GetAsync("key"));
            await Task.Delay(100);
            Assert.Null(await _cache.GetAsync("key"));

            // Then
        }

        [Fact]
        public async Task Set_AbsoluteExpirationHasPrecedenceOverSlidingExpiration()
        {
            // Given
            var now = DateTime.UtcNow;
            var options = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(now.AddMilliseconds(100))
                .SetSlidingExpiration(TimeSpan.FromMilliseconds(200));

            await _cache.SetStringAsync("key", "value", options);

            // When
            Assert.NotNull(await _cache.GetAsync("key"));
            await Task.Delay(50);
            Assert.NotNull(await _cache.GetAsync("key"));

            // Then
            await Task.Delay(100);
            Assert.Null(await _cache.GetAsync("key"));
        }

        [Fact]
        public async Task Set_OverridesExpiration()
        {
            // Given
            var options = new DistributedCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMilliseconds(100));

            await _cache.SetStringAsync("key", "value", options);
            await _cache.SetStringAsync("key", "value");

            // When
            await Task.Delay(150);
            Assert.Equal("value", await _cache.GetStringAsync("key"));

            // Then
        }

        [Fact]
        public async Task Refresh()
        {
            // Given
            var options = new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMilliseconds(100));

            await _cache.SetStringAsync("key", "value", options);

            // When
            await Task.Delay(50);
            await _cache.RefreshAsync("key");
            await Task.Delay(50);
            await _cache.RefreshAsync("key");
            await Task.Delay(50);
            Assert.Equal("value", await _cache.GetStringAsync("key"));

            // Then
            await Task.Delay(150);
            Assert.Null(await _cache.GetStringAsync("key"));
        }

        [Fact]
        public async Task Remove()
        {
            // Given
            await _cache.SetStringAsync("key", "value");

            // When
            await _cache.RemoveAsync("key");

            // Then
            Assert.Null(await _cache.GetStringAsync("key"));
            Assert.Empty(await _fixture.GetKeysAsync(_cache));
        }

        [Fact]
        public async Task Remove_RemovesExpirationAndTags()
        {
            // Given
            var options = new DistributedCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(1));

            await _cache.SetStringAsync("key", "value", options);
            await _cache.TagAsync("key", new[] { "tag1", "tag2" });

            // When
            await _cache.RemoveAsync("key");

            // Then
            Assert.Null(await _cache.GetStringAsync("key"));
            Assert.Empty(await _fixture.GetKeysAsync(_cache));
        }

        [Theory]
        [InlineData(999)]
        [InlineData(1000)]
        [InlineData(1001)]
        [InlineData(1999)]
        [InlineData(2000)]
        [InlineData(2001)]
        public async Task Tag_Many(int count)
        {
            // Given
            var tags = Enumerable.Range(1, count).Select(x => x.ToString()).ToArray();

            await _cache.SetStringAsync("key", "value");

            // When
            await _cache.TagAsync("key", tags);

            // Then
            Assert.Equal(2 + count, await _fixture.GetKeysAsync(_cache).ContinueWith(x => x.Result.Length));
        }

        [Fact]
        public async Task InvalidateTags()
        {
            // Given
            await _cache.SetStringAsync("key", "value");
            await _cache.TagAsync("key", new[] { "tag1", "tag2" });

            // When
            await _cache.InvalidateAsync(new[] { "tag1" });

            // Then
            Assert.Null(await _cache.GetAsync("key"));
            Assert.Empty(await _fixture.GetKeysAsync(_cache));
        }

        [Theory]
        [InlineData(999)]
        [InlineData(1000)]
        [InlineData(1001)]
        [InlineData(1999)]
        [InlineData(2000)]
        [InlineData(2001)]
        public async Task InvalidateTags_Many(int count)
        {
            // Given
            var tags = Enumerable.Range(1, count).Select(x => x.ToString()).ToArray();

            await _cache.SetStringAsync("key", "value");
            await _cache.TagAsync("key", tags);

            // When
            await _cache.InvalidateAsync("10");

            // Then
            Assert.Null(await _cache.GetAsync("key"));
            Assert.Empty(await _fixture.GetKeysAsync(_cache));
        }

        [Fact]
        public async Task InvalidateTags_InvalidatesSingleKey()
        {
            // Given
            await _cache.SetStringAsync("key1", "value1");
            await _cache.SetStringAsync("key2", "value2");
            await _cache.TagAsync("key1", new[] { "tag1", "tag2" });
            await _cache.TagAsync("key2", new[] { "tag2", "tag3" });

            // When
            await _cache.InvalidateAsync(new[] { "tag1" });

            // Then
            Assert.Null(await _cache.GetAsync("key1"));
            Assert.NotNull(await _cache.GetAsync("key2"));
        }

        [Fact]
        public async Task InvalidateTags_InvalidatesMultipleKeys()
        {
            // Given
            await _cache.SetStringAsync("key1", "value1");
            await _cache.SetStringAsync("key2", "value2");
            await _cache.TagAsync("key1", new[] { "tag1", "tag2" });
            await _cache.TagAsync("key2", new[] { "tag2", "tag3" });

            // When
            await _cache.InvalidateAsync(new[] { "tag2" });

            // Then
            Assert.Null(await _cache.GetAsync("key1"));
            Assert.Null(await _cache.GetAsync("key2"));
            Assert.Empty(await _fixture.GetKeysAsync(_cache));
        }

        [Fact]
        public async Task Expiration_RemovesOnlyExpired()
        {
            // Given
            await _cache.SetStringAsync("key1", "value1", new DistributedCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMilliseconds(100)));
            await _cache.SetStringAsync("key2", "value2", new DistributedCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMilliseconds(200)));

            // When
            await Task.Delay(150);
            Assert.Null(await _cache.GetAsync("key1"));

            // Then
            Assert.NotNull(await _cache.GetAsync("key2"));
        }

        [Fact]
        public async Task Expiration_RemovesTags()
        {
            // Given
            var options = new DistributedCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMilliseconds(100));

            await _cache.SetStringAsync("key", "value", options);
            await _cache.TagAsync("key", new[] { "tag1", "tag2" });

            // When
            await Task.Delay(150);
            Assert.Null(await _cache.GetAsync("key"));
            await _fixture.RunExpireAsync(_cache);

            // Then
            Assert.Empty(await _fixture.GetKeysAsync(_cache));
        }
    }
}
