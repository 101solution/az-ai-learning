using RagFoundryDemo;
using RagFoundryDemo.Rag;
using RagFoundryDemo.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// RAG services, reused as-is from the console project. Config is loaded + validated once.
builder.Services.AddSingleton(_ =>
{
    var cfg = RagConfig.Load();
    cfg.Validate();
    return cfg;
});
builder.Services.AddSingleton<AzureClients>();
builder.Services.AddSingleton<Embedder>();
builder.Services.AddSingleton<RagChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
