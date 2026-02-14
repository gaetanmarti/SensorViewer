using immensive;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;

public class Program
{
    // Lock for I2C operations to prevent concurrent access
    private static readonly object _i2cLock = new();

    private static async Task RunWebService (int port, string[]? args = null, CancellationToken token = default)
    {
        WebApplicationBuilder? builder = WebApplication.CreateBuilder(args ?? []);

        Console.WriteLine("Server running on port " + port);

        // Configure Kestrel to use HTTP/1.1 only (avoid HTTP/2 warnings)
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(port, o => o.Protocols = HttpProtocols.Http1);
        });
   
        // Configure Log level to Warning
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Add session services
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        // app.UseHttpsRedirection();

        // Enable sessions
        app.UseSession();

        // =================
        // Generic endpoints
        // =================

        app.MapGet("/api/alive", () => 
            Results.Json(new OkResponse()));

        // =================
        // Sensors endpoints
        // =================

        app.MapGet("/api/sensors",  (HttpContext context) =>
            Global.Sensors.SensorsDelegate(context));

        // =============
        // i2c endpoints
        // =============
        
        app.MapGet("/api/i2c/devices",  (HttpContext context) =>
        {
            lock (_i2cLock)
            {
                return Global.ManagerI2C.DevicesDelegate(context);
            }
        });

        app.MapGet("/api/i2c/device/{address}/specifications", (HttpContext context, string address) =>
        {
            lock (_i2cLock)
            {
                return Global.ManagerI2C.DeviceSpecificationsDelegate(context, address);
            }
        });

        app.MapGet("/api/i2c/device/{address}/data", (HttpContext context, string address) =>
        {
            lock (_i2cLock)
            {
                return Global.ManagerI2C.DeviceDataDelegate(context, address);
            }
        });

        // ==================================
        // Root endpoint (for captive portal)
        // ==================================

        /*
            app.MapGet("/", () =>
            {
                const string html = """
                <!doctype html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>Aquama</title>
                </head>
                <body>
                    <script>
                        window.location.href = "/webapp/index.html";
                    </script>
                    <noscript>
                        <a href="/webapp/index.html">Open application</a>
                    </noscript>
                </body>
                </html>
                """;

                return Results.Content(html, "text/html; charset=utf-8");
            });
       
            IResult RedirectToPortal() =>
                Results.Redirect("/webapp/index.html", permanent: false);
            // Apple
            app.MapGet("/hotspot-detect.html", RedirectToPortal);
            app.MapGet("/library/test/success.html", RedirectToPortal);

            // Android / Chrome
            app.MapGet("/generate_204", RedirectToPortal);
            app.MapGet("/gen_204", RedirectToPortal);

            // Windows
            app.MapGet("/connecttest.txt", RedirectToPortal);
            app.MapGet("/ncsi.txt", RedirectToPortal);
        */

        // ==============
        // Static website
        // ==============
        
        try
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(
                    Path.Combine(AppContext.BaseDirectory, "webapp")),
                RequestPath = "/webapp"
            });
        }
        catch (Exception ex)
        {
            CustomLogger.Log(null, CustomLogger.LogLevel.Error, "Error configuring static file serving: " + ex.Message);
        }
       
        // ================
        // Fallback handler
        // ================ 

        _ = app.MapFallback((HttpContext context) =>
        {
            CustomLogger.Log(null, CustomLogger.LogLevel.Warning,
                "Unhandled request (redirecting to portal): " +
                context.Request.Method + " " + context.Request.Path);

            return Results.Redirect("/webapp/index.html");
        });
                
        await app.RunAsync(token);
    }

    static void DisplayHelp()
    {
        Console.WriteLine("Usage: dotnet backend.dll [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  -h, --help          Display this help message");
        Console.WriteLine("  -p, --port          Specify the port to listen on (default: 8080)");
    }

    public static async Task Main(string[] args)
    {
        // ---------------------------------------------------
        // Parse command line arguments
        // ---------------------------------------------------
        
        // Display help message
        
        if (args.Contains("-h") || args.Contains("--help"))
        {
            DisplayHelp();
            return;
        }

        /* TESTS
        var tof = new TMF882X();
        if (tof.TryDetect(1))
            Console.WriteLine("TMF882X detected.");
        else
            Console.WriteLine("TMF882X not detected.");
        tof.Initialize([], 1);
        for (int i = 0; i < 10; i++)
        {
            var tuple = tof.ReadOnce();
            foreach (var (dist, conf) in tuple)
                Console.WriteLine($"Distance = {dist} mm, Confidence = {conf}");
            Console.WriteLine("---");
            Thread.Sleep(10);
        }
        */
        
        // Parse port number

        int port = 8080;
        int portIndex = -1;

        if (args.Contains("--port"))
            portIndex = Array.IndexOf(args, "--port");
        else if (args.Contains("-p"))
            portIndex = Array.IndexOf(args, "-p");

        if (portIndex != -1)
        {
            if (portIndex + 1 < args.Length)
            {
                if (!int.TryParse(args[portIndex + 1], out port))
                {
                    Console.Error.WriteLine("Invalid port number");
                    return;
                }
            }
            else
            {
                Console.Error.WriteLine("Missing port number");
                return;
            }
        }

        try { 
            // Run server
            await RunWebService (port, [], Global.Cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Server stopped.");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"*** Error: {e.Message}");
        }
    }

}
