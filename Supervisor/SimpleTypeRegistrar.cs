using Spectre.Console.Cli;

namespace Supervisor;

/// <summary>
/// Minimal <see cref="ITypeRegistrar"/> backed by dictionaries.
/// </summary>
/// <remarks>
/// Deliberately not Microsoft.Extensions.DependencyInjection. Every verb — including <c>mcp</c> and
/// <c>hook</c>, which run on the per-session fast path — goes through this, and a container's
/// construction and scanning cost would land squarely on the startup budget (NFR-2) to serve a
/// handful of singletons.
/// </remarks>
internal sealed class SimpleTypeRegistrar : ITypeRegistrar
{
    private readonly Dictionary<Type, object> _instances = [];
    private readonly Dictionary<Type, Type> _implementations = [];
    private readonly Dictionary<Type, Func<object>> _factories = [];

    /// <summary>
    /// Records a mapping. Construction is deferred to <see cref="ITypeResolver.Resolve"/>.
    /// </summary>
    /// <remarks>
    /// Deferral is not an optimization — it is required. Spectre registers its own internal commands
    /// (ExplainCommand among them) and they have no parameterless constructor, so instantiating here
    /// throws during app configuration and every invocation of the binary fails identically.
    /// </remarks>
    public void Register(Type service, Type implementation) =>
        _implementations[service] = implementation;

    public void RegisterInstance(Type service, object implementation) =>
        _instances[service] = implementation;

    /// <summary>Records a factory, invoked at most once on first resolve.</summary>
    public void RegisterLazy(Type service, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[service] = factory;
    }

    public ITypeResolver Build() => new SimpleTypeResolver(_instances, _implementations, _factories);
}

internal sealed class SimpleTypeResolver : ITypeResolver
{
    private readonly Dictionary<Type, object> _instances;
    private readonly Dictionary<Type, Type> _implementations;
    private readonly Dictionary<Type, Func<object>> _factories;

    public SimpleTypeResolver(
        Dictionary<Type, object> instances,
        Dictionary<Type, Type> implementations,
        Dictionary<Type, Func<object>> factories)
    {
        _instances = instances;
        _implementations = implementations;
        _factories = factories;
    }

    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        if (_instances.TryGetValue(type, out var instance))
        {
            return instance;
        }

        if (_factories.TryGetValue(type, out var factory))
        {
            var created = factory();
            _instances[type] = created;   // singleton: the factory runs at most once
            return created;
        }

        // Spectre asks for IEnumerable<T> of its extension points (help providers, and similar)
        // while dispatching, and treats null as a hard failure rather than "none registered".
        // An empty sequence is the contract.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var element = type.GetGenericArguments()[0];
            return _instances.TryGetValue(element, out var single)
                ? CreateArray(element, single)
                : Array.CreateInstance(element, 0);
        }

        var target = _implementations.TryGetValue(type, out var implementation) ? implementation : type;

        return Construct(target);
    }

    private static Array CreateArray(Type element, object single)
    {
        var array = Array.CreateInstance(element, 1);
        array.SetValue(single, 0);
        return array;
    }

    /// <summary>
    /// Builds an instance by satisfying its widest constructor from what is registered.
    /// </summary>
    private object? Construct(Type type)
    {
        var constructor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (constructor is null)
        {
            return null;
        }

        var parameters = constructor.GetParameters();
        var arguments = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            arguments[i] = Resolve(parameters[i].ParameterType);
        }

        return constructor.Invoke(arguments);
    }
}
