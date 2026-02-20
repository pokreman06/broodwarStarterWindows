using Shared;
using Web.Components;
using Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<MyStarcraftBot>();
builder.Services.AddSingleton<StarCraftService>();
builder.Services.AddSingleton<UserPreferencesService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

var starcraftService = app.Services.GetRequiredService<StarCraftService>();
var bot = app.Services.GetRequiredService<MyStarcraftBot>();

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var _ = Task.Run(() =>
{
    app.Run();
});

bot.Connect();


starcraftService.StopAndReset();
