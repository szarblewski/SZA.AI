using Serilog;
using SZA.AI.Proxy.Configurations;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating={Terminating})", e.IsTerminating);
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Unobserved task exception");
    e.SetObserved();
};

// The service must stay up: any failure is logged and the host is rebuilt.
while (true)
{
    try
    {
        var builder = CustomWebApplicationBuilder.CreateBuilder(args);
        var app = CustomWebApplication.Create(builder);
        app.Run();
        break; // graceful shutdown (Ctrl+C / SIGTERM)
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Host crashed, restarting in 3s");
        Thread.Sleep(TimeSpan.FromSeconds(3));
    }
}
