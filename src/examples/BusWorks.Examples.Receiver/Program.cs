using BusWorks;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TracerProvider.Default.GetTracer(builder.Environment.ApplicationName));

builder.Services.AddBusWorks(
    builder.Configuration, 
    typeof(Program).Assembly);

WebApplication app = builder.Build();

await app.RunAsync();
