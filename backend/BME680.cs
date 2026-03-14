// Hookup guide: https://learn.sparkfun.com/tutorials/sparkfun-environmental-sensor-breakout---bme68x-qwiic-hookup-guide
// Datasheet: https://cdn.sparkfun.com/assets/8/a/1/c/f/BME680-Datasheet.pdf
// C# library: https://learn.microsoft.com/en-us/dotnet/api/iot.device.bmxx80.bme680?view=iot-dotnet-latest
// Library doc: https://docs.nanoframework.net/devicesdetails/Bmxx80/README.html

using Iot.Device.Bmxx80;

namespace immensive;

public class BME680 : II2CEnvironmentalSensor
{
    private const int DefaultAddress = 0x77;
    private const byte ChipIdRegister = 0xD0;
    private const byte ExpectedChipId = 0x61;

    protected Iot.Device.Bmxx80.Bme680? _bme680;

    public BME680(int address = DefaultAddress) : base(address)
    {
        Name = "BME680";
    }

    public override Specifications CurrentSpecifications()
    {
        return new Specifications(
            HasTemperature: true,
            HasHumidity: true,
            HasPressure: true,
            HasGas: true
        );
    }

    public override bool TryDetect(int busId, CancellationToken token = default)
    {
        try
        {
            I2C = new I2C(busId, Address, I2C.TransferMode.Auto);
            if (!I2C.Ping(token))
            {
                Reset();
                return false;
            }

            byte chipId = I2C.ReadReg(ChipIdRegister, token);
            if (chipId != ExpectedChipId)
            {
                Reset();
                return false;
            }

            return true;
        }
        catch
        {
            Reset();
            return false;
        }
    }

    public override void Initialize(Dictionary<string, string> config, int busId = -1, CancellationToken token = default)
    {
        base.Initialize(config, busId, token);
        token.ThrowIfCancellationRequested();

        try
        {
            _bme680 = new(I2C.GetI2cDevice());

            _bme680.Reset();
            
            _bme680.HeaterProfile = Bme680HeaterProfile.Profile1;
            _bme680.HeaterIsEnabled = true;

            //_bme680.TemperatureSampling = Sampling.LowPower;
            //_bme680.PressureSampling = Sampling.UltraHighResolution;
            //_bme680.HumiditySampling = Sampling.Standard;
            //_bme680.HeaterIsEnabled = true;
            _bme680.GasConversionIsEnabled = true;
            Initialized = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize BME680 sensor: {ex.Message}", ex);
        }
    }

    public override Dictionary<MeasurementType, float> ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized || _bme680 == null)
            throw new InvalidOperationException("Sensor not initialized. Call Initialize() first.");

        var data = new Dictionary<MeasurementType, float>();

        var readResult = _bme680.Read(); // TODO: Make it async for timeout support 

        if (readResult.Temperature.HasValue)
            data[MeasurementType.Temperature] = (float)readResult.Temperature.Value.DegreesCelsius;
        if (readResult.Humidity.HasValue)
            data[MeasurementType.Humidity] = (float)readResult.Humidity.Value.Percent;
        if (readResult.Pressure.HasValue)
            data[MeasurementType.Pressure] = (float)readResult.Pressure.Value.Hectopascals;
        if (readResult.GasResistance.HasValue)
            data[MeasurementType.Gas] = (float)readResult.GasResistance.Value.Ohms;
        if (readResult.Humidity.HasValue && readResult.GasResistance.HasValue)
        {
            data[MeasurementType.IAQ] = II2CEnvironmentalSensor.CalculateAirQualityIndex(
                (float)readResult.GasResistance.Value.Ohms,
                 (float)readResult.Humidity.Value.Percent);
        }

        return data;
    }

}