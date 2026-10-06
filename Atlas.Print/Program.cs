using Atlas.Print.Services;

using Microsoft.OpenApi.Models;

using Serilog;

IConfiguration bootstrapConfig = new ConfigurationBuilder()
	.AddJsonFile("appsettings.json", optional: true)
	.AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true)
	.AddEnvironmentVariables()
	.Build();

Log.Logger = new LoggerConfiguration()
	.ReadFrom.Configuration(bootstrapConfig)
	.CreateBootstrapLogger();

try
{
	AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
	{
		Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception — terminating: {IsTerminating}", e.IsTerminating);
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

	builder.Services.Configure<HostOptions>(options =>
	{
		options.ShutdownTimeout = TimeSpan.FromSeconds(30);
	});

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

	Log.Information("Atlas.Print starting — EnvironmentName: {EnvironmentName}", app.Environment.EnvironmentName);

	// Swagger only in LOCAL/DEV — DEMO/QA/PROD get nothing registered at all.
	string[] swaggerAllowedEnvironments = ["LOCAL", "DEV"];
	if (swaggerAllowedEnvironments.Contains(app.Environment.EnvironmentName, StringComparer.OrdinalIgnoreCase))
	{
		app.UseSwagger();
		app.UseSwaggerUI(c =>
		{
			c.SwaggerEndpoint("/swagger/v1/swagger.json", "Atlas.Print API V1");
		});
	}

	app.MapControllers();
	app.Run();
}
catch (Exception ex)
{
	Log.Fatal(ex, "Atlas.Print failed to start");
}
finally
{
	Log.CloseAndFlush();
}
