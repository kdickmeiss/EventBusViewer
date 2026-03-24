using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using EventBusViewer.Client.Components;
using EventBusViewer.Client.Services;
using EventBusViewer.ServiceDefaults;
using MudBlazor.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Azure Service Bus — emulator endpoints
builder.Services.AddSingleton(_ =>
    new ServiceBusAdministrationClient(
        "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));

builder.Services.AddSingleton(_ =>
    new ServiceBusClient(
        "Endpoint=sb://localhost:7777;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"));

builder.Services.AddSingleton<QueueService>();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
