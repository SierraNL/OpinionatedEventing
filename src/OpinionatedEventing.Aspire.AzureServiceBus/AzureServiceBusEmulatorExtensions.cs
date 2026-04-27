#nullable enable

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Aspire.Hosting;

/// <summary>
/// Extension methods on <see cref="IDistributedApplicationBuilder"/> for adding an Azure Service Bus
/// emulator resource to an Aspire AppHost.
/// </summary>
public static class AzureServiceBusEmulatorExtensions
{
    /// <summary>
    /// Adds an Azure Service Bus emulator resource.
    /// Referenced projects receive the emulator connection string, which
    /// <c>OpinionatedEventing.AzureServiceBus</c> uses automatically — no managed identity or TLS required.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name; also used as the connection string key.</param>
    /// <returns>A resource builder for the Azure Service Bus resource configured to run as an emulator.</returns>
    public static IResourceBuilder<AzureServiceBusResource> AddAzureServiceBusEmulator(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.AddAzureServiceBus(name).RunAsEmulator();
    }
}
