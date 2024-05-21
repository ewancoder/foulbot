using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace FoulBot.Api;

public static class LoggerExtensions
{
    public sealed class ScopeBuilder<T>
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

        public IDisposable BeginScope()
        {
            return _logger.BeginScope(_scope);
        }
    }

    public static ScopeBuilder<T> AddScoped<T>(this ILogger<T> logger, string key, string value)
    {
        return new ScopeBuilder<T>(logger)
            .AddScoped(key, value);
    }
}
