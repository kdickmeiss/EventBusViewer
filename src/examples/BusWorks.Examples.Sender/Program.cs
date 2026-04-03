using Azure.Messaging.ServiceBus;
using BusWorks.Examples.Sender.Menus;
using BusWorks.Examples.Sender.Menus.Messaging;
using BusWorks.Examples.Sender.Services;
using EventBusViewer.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(_ =>
    new ServiceBusClient(
        "Endpoint=sb://localhost:7777;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));

builder.Services.AddSingleton<MessagingService>();

builder.Services.AddSingleton<MessagingMenu>();
builder.Services.AddSingleton<MainMenu>();

IHost host = builder.Build();

MainMenu mainMenu = host.Services.GetRequiredService<MainMenu>();
await mainMenu.ShowAsync();
