using System.Diagnostics;
using BusWorks.ServiceDefaults;
using BusWorks.Viewer.Components;
using BusWorks.Viewer.Services;
using MudBlazor.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Layer user-settings.json on top of appsettings — created by SettingsService when the user saves.
builder.Configuration.AddJsonFile("user-settings.json", optional: true, reloadOnChange: false);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Settings + shared Service Bus client provider; domain services use the provider directly.
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ServiceBusClientProvider>();
builder.Services.AddSingleton<QueueService>();
builder.Services.AddSingleton<TopicService>();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Automatically open browser on start (only if not running in development)
if (!app.Environment.IsDevelopment())
{
    string url = "http://localhost:5000"; // Change this if you configure a different port
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        // Optionally log or handle the error
    }
}

await app.RunAsync();
