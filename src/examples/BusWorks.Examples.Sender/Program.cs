using BusWorks;
using BusWorks.Examples.Sender.Menus;
using BusWorks.Examples.Sender.Menus.Messaging;
using BusWorks.Examples.Sender.Services;
using BusWorks.Options;
using BusWorks.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(TracerProvider.Default.GetTracer(builder.Environment.ApplicationName));

BusWorksOptions busWorksOptions = new()
{
    AuthenticationType = EventBusAuthenticationType.ConnectionString,
    ConnectionString = new ConnectionStringOptions
    {
        ConnectionString =
            "Endpoint=sb://localhost:7777;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
    }
};

builder.Services.AddBusWorks(busWorksOptions, typeof(Program).Assembly);
builder.Services.AddSingleton<MessagingService>();
builder.Services.AddSingleton<MessagingMenu>();
builder.Services.AddSingleton<MainMenu>();

IHost host = builder.Build();

MainMenu mainMenu = host.Services.GetRequiredService<MainMenu>();
await mainMenu.ShowAsync();
