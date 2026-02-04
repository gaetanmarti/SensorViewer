// Time-of-Flight distance sensors TMF882X
// Hookup guide: https://learn.sparkfun.com/tutorials/qwiic-dtof-imager-tmf882x-hookup-guide/all
// Datasheet: https://cdn.sparkfun.com/assets/learn_tutorials/2/2/8/9/TMF882X_DataSheet.pdf

namespace immensive;

public class TMF882X: II2CDevice
{
    private I2C? _i2c;
    
    public TMF882X(int address = 0x41) : base(address)
    {
        Name = "TMF882X Time-of-Flight Sensor";
    }

    public override bool TryDetect(int busId)
    {
        try
        {
            _i2c = new I2C(busId, Address);
            
            // According to TMF882X datasheet:
            // Register 0x00: APPID - Application ID
            // Expected values: 0x03 (ROM), 0x80 (RAM), 0xC0 (APP)
            // We check for any valid TMF882X app ID
            
            // Write register address 0x00
            _i2c.WriteByte(0x00);
            
            // Read the APPID register
            byte appId = _i2c.ReadByte();
            
            // Valid TMF882X APPID values according to datasheet
            // 0x03 = ROM bootloader mode
            // 0x80 = RAM bootloader mode  
            // 0xC0 = Application mode
            bool isValidAppId = (appId == 0x03 || appId == 0x80 || appId == 0xC0);
            return isValidAppId;
        }
        catch
        {
            _i2c?.Dispose();
            _i2c = null;
            return false;
        }
    }
}