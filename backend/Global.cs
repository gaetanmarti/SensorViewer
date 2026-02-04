namespace immensive;

// Global static class to store singleton variables
public static class Global
{
    // Lazy-initialized singletons
    private static Sensors? _sensors;
    public static Sensors Sensors => _sensors ??= new Sensors();

    private static ManagerI2C? _managerI2C;
    public static ManagerI2C ManagerI2C => _managerI2C ??= new ManagerI2C();

    private static CustomLogger? _logger;
    public static CustomLogger Logger => _logger ??= new CustomLogger();

    public static CancellationTokenSource Cts { get; } = new CancellationTokenSource();
}
