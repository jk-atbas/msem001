using System.Text;
using Msem001.Watchdog.Hosting;
using Serilog;

#pragma warning disable RS0030 // Composition root: runs before the abstractions are available via DI.

// Windows services start in C:\Windows\System32 - resolve relative paths against the service directory.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

#pragma warning restore RS0030

// Machine specific secrets (Jenkins secret, SMTP credentials) - not under version control.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Trace);
builder.Logging.AddSerilog(Log.Logger, dispose: true);

builder.Services.AddWindowsService(options => options.ServiceName = "Msem001Watchdog");
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});
builder.Services.AddWatchdog(builder.Configuration);

try
{
    await builder.Build().RunAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    Log.Fatal(ex, "MSEM001 Watchdog wurde unerwartet beendet");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
