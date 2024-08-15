using Microsoft.Extensions.Logging;

namespace FoulBot.Domain;

public interface IScopedLogger
{
    IScopedLogger AddScoped(string key, object? value);
    IDisposable BeginScope();
}

public static class LoggerExtensions
{
    public sealed class ScopeBuilder<T> : IScopedLogger
    {
        private readonly ILogger<T> _logger;
        private readonly Dictionary<string, object?> _scope
            = new Dictionary<string, object?>();

        public ScopeBuilder(ILogger<T> logger)
        {
            _logger = logger;
        }

        public ScopeBuilder<T> AddScoped(string key, object? value)
        {
            _scope.TryAdd(key, value);
            return this;
        }

        public IDisposable? BeginScope()
        {
            return _logger.BeginScope(_scope);
        }

        IScopedLogger IScopedLogger.AddScoped(string key, object? value)
        {
            _scope.TryAdd(key, value);
            return this;
        }
    }

    public static ScopeBuilder<T> AddScoped<T>(this ILogger<T> logger)
        => new ScopeBuilder<T>(logger);

    public static ScopeBuilder<T> AddScoped<T>(this ILogger<T> logger, string key, object? value)
    {
        return new ScopeBuilder<T>(logger)
            .AddScoped(key, value);
    }
}
