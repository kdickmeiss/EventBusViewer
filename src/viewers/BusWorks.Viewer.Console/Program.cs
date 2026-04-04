using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using BusWorks.ServiceDefaults;
using BusWorks.Viewer.Console.Menus;
using BusWorks.Viewer.Console.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QueueMenu = BusWorks.Viewer.Console.Menus.Queue.QueueMenu;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(_ =>
    new ServiceBusAdministrationClient(
        "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));

builder.Services.AddSingleton(_ =>
    new ServiceBusClient(
        "Endpoint=sb://localhost:7777;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));

builder.Services.AddSingleton<QueueService>();
builder.Services.AddSingleton<QueueMenu>();
builder.Services.AddSingleton<MainMenu>();

IHost host = builder.Build();

MainMenu mainMenu = host.Services.GetRequiredService<MainMenu>();
await mainMenu.ShowAsync();
