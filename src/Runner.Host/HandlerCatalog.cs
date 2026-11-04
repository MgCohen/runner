using System.Reflection;
using Runner.Abstractions;

namespace Runner.Host;

/// <summary>An assembly that the catalog scans for handlers.</summary>
public sealed record HandlerAssembly(Assembly Assembly);

/// <summary>One handler class and its <c>[On]</c> filters.</summary>
public sealed record HandlerDescriptor(Type Type, IReadOnlyList<OnAttribute> Filters)
{
    /// <summary>The stable key of the handler in the local store.</summary>
    public string Key => Type.FullName ?? Type.Name;

    public bool Matches(InboxEvent e) => Filters.Any(f => f.Matches(e));
}

/// <summary>
/// Finds each <see cref="IEventHandler"/> class with <see cref="OnAttribute"/> in the
/// registered handler assemblies (contract B, discovery).
/// </summary>
public sealed class HandlerCatalog
{
    public HandlerCatalog(IEnumerable<HandlerAssembly> assemblies)
    {
        Handlers = assemblies
            .Select(a => a.Assembly)
            .Distinct()
            .SelectMany(Scan)
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<HandlerDescriptor> Handlers { get; }

    public IReadOnlyList<HandlerDescriptor> Match(InboxEvent e) => Handlers.Where(h => h.Matches(e)).ToList();

    private static IEnumerable<HandlerDescriptor> Scan(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEventHandler).IsAssignableFrom(t))
            .Select(t => new HandlerDescriptor(t, t.GetCustomAttributes<OnAttribute>().ToList()))
            .Where(h => h.Filters.Count > 0);
}
