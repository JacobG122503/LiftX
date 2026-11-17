using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using LiftX;
using LiftX.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<RestTimer>();
builder.Services.AddSingleton<Dialogs>();

var host = builder.Build();

// Load saved data synchronously from localStorage before the first render
host.Services.GetRequiredService<AppState>().Load();

await host.RunAsync();
