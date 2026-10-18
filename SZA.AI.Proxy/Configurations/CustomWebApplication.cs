using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Scalar.AspNetCore;
using SZA.AI.Proxy.Routes;

namespace SZA.AI.Proxy.Configurations
{
    public static class CustomWebApplication
    {
        public static WebApplication Create(this WebApplicationBuilder builder)
        {
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference();
            }

            //app.UseHttpsRedirection();

            // Last line of defense: no request exception may escape and bring the service down.
            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    // Client disconnected.
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Clear();
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"error\":\"Internal proxy error.\"}").ConfigureAwait(false);
                    }
                }
            });

            app.UseCors("AllowOrigin");

            
            // Monta as rotas da aplicação
            MapRoutes.RoutesBuilder(app);

            app.Lifetime.ApplicationStarted.Register(() => RegisterRuntimePort(app));

            return app;
        }

        private static void RegisterRuntimePort(WebApplication app)
        {
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses.ToArray() ?? [];

            app.Logger.LogInformation("SZA.AI.Proxy listening on: {Addresses}", string.Join(", ", addresses));

            try
            {
                var dataPath = Path.Combine(AppContext.BaseDirectory, "Data");
                Directory.CreateDirectory(dataPath);
                File.WriteAllText(
                    Path.Combine(dataPath, "runtime.json"),
                    JsonSerializer.Serialize(new { addresses, startedAt = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Could not write runtime.json");
            }
        }
    }
}
