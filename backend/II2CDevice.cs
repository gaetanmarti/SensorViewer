namespace immensive;

// Interface for I2C devices
public abstract class II2CDevice (int address)
{
    // I2C device address
    public int Address { get; } = address;
    
    // Device name
    public string Name { get; protected set; } = "";

    protected I2C? _i2c = null;

    // Associated I2C instance
    public I2C I2C { 
        get {
            if (_i2c == null)
                throw new InvalidOperationException($"I2C instance not set for device {Name} at address 0x{Address:X2}");
            return _i2c;
        } 
        protected set => _i2c = value;
    }

    // Reset the i2c instance (e.g. when device is not responding)
    protected void Reset ()
    {
        _i2c?.Dispose();
        _i2c = null;
    }

    // Try to detect the device on the specified bus
    // If detected and responding, set I2C property and return true; otherwise return false
    public virtual bool TryDetect(int busId) => false;

    // Equality operator based on Address and Name
    public static bool operator ==(II2CDevice? left, II2CDevice? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Address == right.Address && left.Name == right.Name;
    }

    // Initialize the device
    public virtual void Initialize(Dictionary<string, string> config, int busId = -1)
    {
        if (busId >= 0 && _i2c == null)
        {
            I2C = new I2C(busId, Address);
            return;
        }
        if (_i2c == null)
            throw new InvalidOperationException($"Cannot initialize device {Name} at address 0x{Address:X2}: invalid busId or I2C instance already set.");
    }

    // Inequality operator
    public static bool operator !=(II2CDevice? left, II2CDevice? right)
    {
        return !(left == right);
    }

    // Override Equals for consistency
    public override bool Equals(object? obj)
    {
        return obj is II2CDevice device && Address == device.Address && Name == device.Name;
    }

    // Override GetHashCode for consistency
    public override int GetHashCode()
    {
        return HashCode.Combine(Address, Name);
    }
}

public class UnknownII2CDevice : II2CDevice
{
    public UnknownII2CDevice(int address): base(address)
    {
        Name = "<Unknown Device>";
    }
}
