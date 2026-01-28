# SensorViewer - Backend

A lightweight RESTful web service built with ASP.NET Core 9.0 for monitoring real-time system resource usage and sensor data.

## Features

- **Real-time System Monitoring** - CPU usage, RAM usage, and temperature sensors
- **Cross-platform Support** - macOS (with additional sensors via Stats.app)
- **REST API** - Simple HTTP endpoints for sensor data retrieval
- **JSON Serialization** - Clean JSON responses with proper enum string conversion

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- macOS, Linux, or Windows
- **Optional (macOS)**: [Stats.app](https://github.com/exelban/stats) for extended temperature sensor support

## Quick Start

### Build the Project

```bash
dotnet build backend.csproj
```

### Run the Server

```bash
dotnet run
```

By default, the server runs on port **8080**.

### Command Line Options

```
Options:
  -h, --help          Display help message
  -p, --port <port>   Specify the port to listen on (default: 8080)
  --dev               Start application in development mode
```

> Note: Development mode uses alternate configuration for debug purposes.

## Project Structure

```
├── Program.cs                 # Main application entry point and API endpoints
├── Global.cs                  # Global application state and initialization
├── Sensors.cs                 # Sensors API endpoint logic
├── SystemResourceUsage.cs     # Abstract base class for system monitoring
├── MacOSSRU.cs               # macOS-specific sensor implementation
├── LinuxSRU.cs               # Linux-specific sensor implementation (stub)
├── WindowsSRU.cs             # Windows-specific sensor implementation (stub)
├── SecureSerializer.cs        # Secure serialization utilities
├── Helpers.cs                 # Utility functions
├── Logger.cs                  # Custom logging implementation
├── backend.csproj             # Project configuration
├── backend.sln                # Solution file
├── appsettings.json           # Application settings
├── appsettings.Development.json  # Development-specific settings
└── API.md                     # Detailed API documentation
```

## API Overview

For detailed API documentation with request/response examples, see [API.md](API.md).

### Quick API Reference

#### Health Check
```bash
GET /api/alive
```

#### Get Sensor Data
```bash
GET /api/sensors
```

Returns JSON with all available sensors:
```json
{
  "sensors": [
    {
      "name": "CPU Usage",
      "unit": "Percent",
      "value": "45.30"
    },
    {
      "name": "RAM Usage",
      "unit": "Percent",
      "value": "68.50"
    },
    {
      "name": "CPU Core 1",
      "unit": "Temperature",
      "value": "52.00"
    }
  ]
}
```

## Sensor Support

### macOS
- **CPU Usage** - Real-time processor utilization percentage
- **RAM Usage** - Memory utilization percentage
- **Temperature Sensors** - CPU cores, GPU, SSD, memory, and airflow (requires Stats.app)

### Linux & Windows
- Basic CPU and RAM monitoring (extensible via platform-specific implementations)

## Configuration

### Application Settings
- `appsettings.json` - General application configuration
- `appsettings.Development.json` - Development-specific settings

## Development

Written in C# with ASP.NET Core 9.0, this project demonstrates:
- Platform-specific system resource monitoring
- Abstract factory pattern for cross-platform support
- RESTful API design
- JSON serialization with proper enum handling

## macOS Temperature Monitoring

For extended temperature sensor data on macOS, install [Stats.app](https://github.com/exelban/stats):

```bash
brew install stats
```

The application will automatically detect and use the SMC (System Management Controller) binary from Stats.app to read:
- CPU core temperatures
- GPU temperature
- SSD temperatures
- Memory temperature
- Airflow sensors

## Technical Notes

- Session configuration: 30-minute timeout
- HTTP/1.1 protocol
- ListenAnyIP configuration for network accessibility
- Enum serialization using `JsonStringEnumConverter`

---

GMA, 2026-01-28
