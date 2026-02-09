using immensive;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders;

public class Program
{
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
            Global.ManagerI2C.DevicesDelegate(context));

        app.MapGet("/api/i2c/device/{address}/specifications", (HttpContext context, string address) =>
        {
            try
            {
                if (!TryParseI2cAddress(address, out int addr))
                    return Results.BadRequest(new { ok = false, error = "Invalid I2C address." });

                if (!Global.ManagerI2C.TryGetDistanceSensor(addr, out var sensor, context.RequestAborted) || sensor == null)
                    return Results.NotFound(new { ok = false, error = "Distance sensor not found." });

                var specs = sensor.CurrentSpecifications();
                return Results.Json(new
                {
                    ok = true,
                    address = sensor.Address,
                    name = sensor.Name,
                    specifications = specs
                });
            }
            catch (OperationCanceledException)
            {
                return Results.Json(new { ok = false, error = "Operation cancelled." });
            }
            catch (Exception ex)
            {
                CustomLogger.Log(null, CustomLogger.LogLevel.Error, $"Error in /specifications: {ex.Message}");
                return Results.Json(new { ok = false, error = "Internal server error.", details = ex.Message });
            }
        });

        app.MapGet("/api/i2c/device/{address}/measure", (HttpContext context, string address) =>
        {
            try
            {
                if (!TryParseI2cAddress(address, out int addr))
                    return Results.BadRequest(new { ok = false, error = "Invalid I2C address." });

                if (!Global.ManagerI2C.TryGetDistanceSensor(addr, out var sensor, context.RequestAborted) || sensor == null)
                    return Results.NotFound(new { ok = false, error = "Distance sensor not found." });

                var measurement = sensor.ReadOnce(token: context.RequestAborted)
                    .Select(m => new { distMM = m.distMM, confidence = Math.Round(m.confidence, 3) })
                    .ToList();
                return Results.Json(new
                {
                    ok = true,
                    address = sensor.Address,
                    name = sensor.Name,
                    measurement
                });
            }
            catch (OperationCanceledException)
            {
                return Results.Json(new { ok = false, error = "Operation cancelled." });
            }
            catch (TimeoutException ex)
            {
                CustomLogger.Log(null, CustomLogger.LogLevel.Warning, $"Timeout in /measure: {ex.Message}");
                return Results.Json(new { ok = false, error = "Measurement timeout.", details = ex.Message });
            }
            catch (Exception ex)
            {
                CustomLogger.Log(null, CustomLogger.LogLevel.Error, $"Error in /measure: {ex.Message}");
                return Results.Json(new { ok = false, error = "Internal server error.", details = ex.Message });
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

    private static bool TryParseI2cAddress(string addressText, out int address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(addressText))
            return false;

        addressText = addressText.Trim();
        if (addressText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(addressText[2..], System.Globalization.NumberStyles.HexNumber, null, out address);

        return int.TryParse(addressText, out address);
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
