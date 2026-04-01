using System.Runtime.InteropServices;
using Aspire.Hosting.Azure;
using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<AzureServiceBusResource> serviceBus = builder
    .AddAzureServiceBus("eventBus")
    .RunAsEmulator(emulator =>
    {
        emulator
            .WithHostPort(7777) // Main AMQP endpoint (maps to container port 5672)
            //.WithEndpoint(name: "admin", port: 5300, targetPort: 5300, isProxied: false) // Admin API endpoint
            .WithHttpEndpoint(port: 5300, targetPort: 5300, name: "admin",
                isProxied: false) // Admin API endpoint - HTTP
            .WithImageTag("latest");
    });

serviceBus.AddServiceBusQueue("parking-spot-reserved");
serviceBus.AddServiceBusTopic("parking-ticket-bought").AddServiceBusSubscription("email-notifications");

// On ARM Macs, replace SQL Server with SQL Edge (which supports ARM64)
if (RuntimeInformation.ProcessArchitecture is (Architecture.Arm64 or Architecture.Arm))
{
    // Find the SQL Server container that was just created by RunAsEmulator
    IResource? sqlContainerResource = builder.Resources.FirstOrDefault(r => r.Name == "eventBus-mssql");

    // Remove the existing SQL Server image annotation and replace with SQL Edge
    ContainerImageAnnotation? imageAnnotation =
        sqlContainerResource?.Annotations.OfType<ContainerImageAnnotation>().FirstOrDefault();
    if (imageAnnotation != null)
    {
        sqlContainerResource?.Annotations.Remove(imageAnnotation);
        sqlContainerResource?.Annotations.Add(new ContainerImageAnnotation
        {
            Registry = "mcr.microsoft.com",
            Image = "azure-sql-edge",
            Tag = "latest"
        });
    }
}

builder
    .AddProject<BusWorks_Viewer>("viewer")
    .WaitFor(serviceBus)
    .WithReference(serviceBus);

builder.AddProject<BusWorks_Examples_Receiver>("exampleReceiver")
    .WaitFor(serviceBus)
    .WithReference(serviceBus);

// builder.AddProject<EventBusViewer_Tool>("tool").
//     WaitFor(serviceBus)
//     .WithReference(serviceBus);
//     

await builder.Build().RunAsync();
