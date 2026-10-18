using Microsoft.AspNetCore.DataProtection;
using Serilog;
using SZA.AI.Proxy.Providers;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.Configurations
{
    public static class CustomWebApplicationBuilder
    {
        

        public static WebApplicationBuilder CreateBuilder(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(
                    path: "logs/sza-ai-proxy-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();


            var builder = WebApplication.CreateBuilder(args);

            builder.Host.UseSerilog();
            builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

            ConfigurePort(builder, args);

            // Long/slow generations must not be cut by Kestrel's default data-rate and idle limits.
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Limits.MinResponseDataRate = null;
                k.Limits.MinRequestBodyDataRate = null;
                k.Limits.KeepAliveTimeout = TimeSpan.FromHours(1);
                k.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowOrigin", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .WithExposedHeaders("Access-Control-Allow-Private-Network");
                });
            });





            // Add services to the container.
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();


            




            MountScopeds(builder);

            return builder;
        }

        private static void ConfigurePort(WebApplicationBuilder builder, string[] args)
        {
            // ASPNETCORE_URLS / --urls keep priority when explicitly defined.
            if (!string.IsNullOrWhiteSpace(builder.Configuration[WebHostDefaults.ServerUrlsKey]))
                return;

            var options = ProxyOptions.Resolve(args, builder.Configuration);

            // Kestrel does not accept port 0 with "localhost".
            var host = options.Port == 0 ? "127.0.0.1" : "localhost";
            builder.WebHost.UseUrls($"http://{host}:{options.Port}");
        }

        private static void MountScopeds(WebApplicationBuilder builder)
        {
            var keysPath = Path.Combine(AppContext.BaseDirectory, "Data", "keys");
            builder.Services.AddDataProtection()
                .SetApplicationName("SZA.AI.Proxy")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

            builder.Services.AddSingleton<ISecret_Store, Secret_Store>();
            builder.Services.AddSingleton<IProvider_Registry, Provider_Registry>();
            builder.Services.AddSingleton<IModel_Registry, Model_Registry>();
            builder.Services.AddSingleton<IProvider_Template_Registry, Provider_Template_Registry>();
            builder.Services.AddHttpClient("provider", c => c.Timeout = Timeout.InfiniteTimeSpan)
                            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                            {
                                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                                ConnectTimeout = TimeSpan.FromSeconds(30),
                                EnableMultipleHttp2Connections = true
                            });
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<IImage_Describer, Image_Describer>();
            builder.Services.AddScoped<IProviderClient, Provider_Client>();
            builder.Services.AddScoped<IModel_Sync, Model_Sync>();
            builder.Services.AddScoped<IOllama_Service, Ollama_Service>();
        }
    }
}
