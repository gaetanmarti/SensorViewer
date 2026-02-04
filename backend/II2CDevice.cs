namespace immensive;

// Interface for I2C devices
public abstract class II2CDevice (int address)
{
    // I2C device address
    public int Address { get; } = address;
    
    // Device name
    public string Name { get; protected set; } = "";

    // Associated I2C instance
    public I2C? I2C { get; protected set; } = null;

    // Try to detect the device on the specified bus
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
