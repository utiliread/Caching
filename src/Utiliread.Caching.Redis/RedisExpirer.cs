using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;
using Utiliread.Caching.Redis.Scripts;
using RedisCacheOptions = Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions;

namespace Utiliread.Caching.Redis
{
    internal class RedisExpirer : BackgroundService
    {
        private readonly RedisCacheOptions _options;
        private readonly ILogger<RedisExpirer> _logger;
        private readonly LuaScripts _scripts;
        private ConnectionMultiplexer _connection;

        public RedisExpirer(IOptions<RedisCacheOptions> options, ILogger<RedisExpirer> logger = null)
        {
            _options = options.Value;
            _logger = logger;
            _scripts = new LuaScripts(_options.InstanceName ?? string.Empty);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await EnsureConnectionAsync().ConfigureAwait(false);
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunExpireAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger?.LogError(e, "Unhandled exception");
                }

                await Task.Delay(60_000, stoppingToken).ConfigureAwait(false);
            }
        }

        internal async Task EnsureConnectionAsync()
        {
            if (_connection is null)
            {
                if (_options.ConfigurationOptions is not null)
                {
                    _connection = await ConnectionMultiplexer.ConnectAsync(_options.ConfigurationOptions).ConfigureAwait(false);
                }
                else
                {
                    _connection = await ConnectionMultiplexer.ConnectAsync(_options.Configuration).ConfigureAwait(false);
                }
            }
        }

        internal Task RunExpireAsync()
        {
            var database = _connection.GetDatabase();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return database.ScriptEvaluateAsync(_scripts.Expire, values: new RedisValue[] { now });
        }
    }
}
