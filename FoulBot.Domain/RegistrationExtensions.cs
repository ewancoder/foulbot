using FoulBot.Domain.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoulBot.Domain;

public static class RegistrationExtensions
{
    // Unfortunately AddSingleton won't dispose IAsyncDisposable as of now.
    // This is a limitation of .NET DI.
    // However it's not a big of a deal. When we need to dispose singletons,
    // the whole app is shutting down anyway.
    public static IServiceCollection AddFoulBotDomain(this IServiceCollection services)
    {
        return services
            .AddSingleton(TimeProvider.System)
            .AddSingleton<ISharedRandomGenerator, SharedRandomGenerator>()
            .AddSingleton<IBotDelayStrategy, BotDelayStrategy>()
            .AddTransient<IFoulBotFactory, FoulBotFactory>()
            .AddTransient<IFoulChatFactory, FoulChatFactory>();
    }

    public static IServiceCollection AddChatPool<TDuplicateMessageHandler>(
        this IServiceCollection services, string key)
        where TDuplicateMessageHandler : class, IDuplicateMessageHandler
    {
        return services
            .AddTransient<TDuplicateMessageHandler>()
            .AddKeyedScoped(key, (provider, _) => new ChatPool(
                provider.GetRequiredService<ILogger<ChatPool>>(),
                provider.GetRequiredService<IFoulChatFactory>(),
                provider.GetRequiredService<TDuplicateMessageHandler>(),
                provider.GetRequiredService<IAllowedChatsProvider>())); // TODO: Rewrite into ChatPool factory.
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection Decorate<TInterface, TDecorator>(this IServiceCollection services)
      where TDecorator : TInterface
    {
        var wrappedDescriptor = services.LastOrDefault(
            s => s.ServiceType == typeof(TInterface))
            ?? throw new InvalidOperationException($"{typeof(TInterface).Name} is not registered.");

        var objectFactory = ActivatorUtilities.CreateFactory(
            typeof(TDecorator),
            [typeof(TInterface)]);

        return services.Replace(ServiceDescriptor.Describe(
            typeof(TInterface),
            s => (TInterface)objectFactory(s, [s.CreateInstance(wrappedDescriptor)]),
            wrappedDescriptor.Lifetime));
    }

    private static object CreateInstance(this IServiceProvider services, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance != null)
            return descriptor.ImplementationInstance;

        if (descriptor.ImplementationFactory != null)
            return descriptor.ImplementationFactory(services);

        return ActivatorUtilities.GetServiceOrCreateInstance(
            services, descriptor.ImplementationType ?? throw new InvalidOperationException("Implementation type is null, cannot decorate it."));
    }
}
