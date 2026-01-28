using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;

namespace immensive;

public class Sensors
{
 // Lazy-initialized singletons
    private static SystemResourceUsage? _sru;
    public static SystemResourceUsage Sru => _sru ??= SystemResourceUsage.Get();

    // ========================
    // Authentication endpoints
    // ========================
    
    public class SysInfoResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("sensors")]
        public required List<SystemResourceUsage.Sensor> Sensors { get; set; }
    }

    public IResult SensorsDelegate (HttpContext context)
    {
        Sensors.Sru.Update ();
        return Results.Json (new SysInfoResponse 
        { 
            Sensors = Sensors.Sru.SensorList.List
        });
    }                       
    
}