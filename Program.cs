using Atlas.Print.Services;

using Microsoft.OpenApi.Models;

using Serilog;

AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
	Console.WriteLine($"UNHANDLED EXCEPTION: {e.ExceptionObject}");
	Console.ReadKey();
};

Serilog.Debugging.SelfLog.Enable(msg => System.Diagnostics.Debug.WriteLine(msg));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((ctx, services, lc) =>
	lc.ReadFrom.Configuration(ctx.Configuration)
	  .ReadFrom.Services(services)
	  .Enrich.FromLogContext()
	  .Enrich.WithThreadId()
	  .Enrich.WithProcessId());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
	c.SwaggerDoc("v1", new OpenApiInfo { Title = "Atlas.Print API", Version = "v1" });
});

builder.Services.AddSingleton<BrowserPool>();
builder.Services.AddSingleton<IBrowserPool>(sp => sp.GetRequiredService<BrowserPool>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BrowserPool>());
builder.Services.AddSingleton<PlaywrightPrintRenderer>();

WebApplication app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
	c.SwaggerEndpoint("/swagger/v1/swagger.json", "Atlas.Print API V1");
});

app.MapControllers();
app.Run();
