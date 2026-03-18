# SensorViewer - Backend

A lightweight RESTful web service built with ASP.NET Core 9.0 for monitoring real-time system resource usage, I2C sensor data, and virtual computed sensors (human presence / distance detection).

## Features

- **Real-time System Monitoring** — CPU usage, RAM usage, and temperature sensors
- **I2C Device Management** — Auto-detection and registration of I2C peripherals
- **Virtual Sensors** — Computed sensors derived from physical ones (human presence from thermal, human distance from ToF)
- **Cross-platform Support** — macOS, Linux (Raspberry Pi), Windows
- **REST API** — Simple HTTP endpoints for sensor specifications and live data
- **JSON Serialization** — Clean JSON responses with proper enum string conversion

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- macOS, Linux, or Windows
- **Raspberry Pi** (for I2C sensors): Linux with I2C enabled (`/dev/i2c-*`)
- **Optional (macOS)**: [Stats.app](https://github.com/exelban/stats) for extended temperature sensor support

## Quick Start

### Build

```bash
dotnet build backend.csproj
```

### Run locally

```bash
dotnet run
```

The server listens on `http://[::]:8080` (all interfaces, port 8080).

### Run in development mode

```bash
dotnet run -- --dev --dataPath ../data
```

## Project Structure

```
backend/
├── Program.cs                      # Entry point, HTTP routing, API endpoints
├── Global.cs                       # Global application state and initialization
├── Sensors.cs                      # Sensors API endpoint logic
├── SystemRessourceUsage.cs         # Abstract base class for system monitoring
├── MacOSSRU.cs                     # macOS-specific sensor implementation
├── LinuxSRU.cs                     # Linux-specific sensor implementation
├── WindowsSRU.cs                   # Windows-specific sensor implementation
├── Helpers.cs                      # Utility functions
├── Logger.cs                       # Custom logging implementation
│
├── I2C.cs                          # Low-level I2C bus abstraction
├── II2CDevice.cs                   # Base interfaces and abstract classes:
│                                   #   II2CDevice, II2CDistanceSensor,
│                                   #   II2CThermalSensor, II2CHumanPresenceSensor,
│                                   #   II2CHumanDistanceSensor, II2CEnvironmentalSensor
├── ManagerI2C.cs                   # I2C device registry, auto-detection, API delegates
│
├── TMF882X.cs                      # AMS TMF882X ToF distance sensor (3×3, short/long range)
├── TMF882XFirmware.cs              # TMF882X embedded firmware blob
├── VL53L5CX.cs                     # STM VL53L5CX ToF distance sensor (8×8)
├── VL53L5CXFirmware.cs             # VL53L5CX embedded firmware blob
├── AMG88XX.cs                      # Panasonic AMG88XX thermal sensor (8×8)
├── MLX90640.cs                     # Melexis MLX90640 thermal sensor (32×24)
├── STHS34PF80.cs                   # ST STHS34PF80 IR human presence sensor
├── BME680.cs                       # Bosch BME680 environmental sensor
│
├── ThermalHumanPresenceSensor.cs   # Virtual sensor: human presence from thermal camera
├── DistanceHumanPresenceSensor.cs  # Virtual sensor: human presence/distance from ToF
├── DistanceHumanTracker.cs         # ScreenVisitorTracker algorithm
│
├── backend.csproj
├── backend.sln
├── appsettings.json
├── appsettings.Development.json
└── API.md                          # Detailed REST API documentation

scripts/
├── backendInstall.sh               # Install systemd service on the Pi
├── backendRemove.sh                # Remove systemd service from the Pi
└── backendStatus.sh                # Check service status and journal
```

## API Overview

For detailed API documentation with request/response examples, see [API.md](API.md).

### Device Types

| Type | Description |
|------|-------------|
| `Distance` | Raw ToF distance sensor (e.g. TMF882X, VL53L5CX) |
| `Thermal` | Thermal camera (e.g. AMG88XX, MLX90640) |
| `HumanPresence` | IR human presence sensor or virtual sensor from thermal |
| `HumanDistance` | Virtual sensor: visitor detection + 3-D position from ToF |
| `Environmental` | Environmental sensor (e.g. BME680) |

## Sensor Support

### I2C Sensors (Linux / Raspberry Pi)

| Sensor | Type | I2C Address | Notes |
|--------|------|-------------|-------|
| TMF882X | Distance (3×3 ToF) | 0x41 | Short/long range mode |
| VL53L5CX | Distance (8×8 ToF) | 0x29 | Requires firmware upload |
| AMG88XX | Thermal (8×8) | 0x68 / 0x69 | |
| MLX90640 | Thermal (32×24) | 0x33 | |
| STHS34PF80 | Human Presence (IR) | 0x5A | |
| BME680 | Environmental | 0x76 / 0x77 | Temperature, humidity, pressure, gas |

### Virtual Sensors (auto-created)

Virtual sensors are registered automatically at startup alongside their physical counterpart. Their address is `physical_address + 0x80` (e.g. TMF882X at `0x41` → virtual at `0xC1`).

- **`ThermalHumanPresenceSensor`** — blob detection on the thermal frame
- **`DistanceHumanPresenceSensor`** — runs `ScreenVisitorTracker` for visitor detection and 3-D position estimation

### ScreenVisitorTracker Algorithm

Purpose-built for the interactive screen use-case (sensor mounted facing visitors, static background at ~1.5–2 m with low-confidence readings):

1. **Filter** — discard cells with zero distance or confidence below threshold
2. **Classify** — a valid cell is foreground when its distance is ≥ Δ mm closer than the learned per-cell background
3. **Learn** — non-foreground valid cells update the per-cell background via a slow EMA
4. **Presence** — counter-based state machine: presence ON after N consecutive candidate frames, OFF after M frames without candidate
5. **Position** — confidence-weighted centroid of foreground cells, projected to 3-D (mm), smoothed with EMA

### System Resources

| Platform | CPU | RAM | Temperature |
|----------|-----|-----|-------------|
| macOS | ✓ | ✓ | ✓ (requires Stats.app) |
| Linux | ✓ | ✓ | ✓ |
| Windows | ✓ | ✓ | — |

## Deployment to Raspberry Pi

### 1. Build & copy

VS Code tasks handle both steps:

| Task | Description |
|------|-------------|
| `build and copy direct` | Build then `scp` to `192.168.4.50` |
| `build and copy tailscale` | Build then `scp` via Tailscale |

Or manually:
```bash
dotnet build backend.csproj
scp -r bin/Debug/net9.0/* pi@<ip>:~/backend/bin/Debug/net9.0/
```

### 2. Manage the service

Scripts in `../scripts/` automate systemd service management (service name: `sensorviewer`).

**Install or update:**
```bash
../scripts/backendInstall.sh --host 192.168.2.149
```
Creates a systemd service that auto-starts at boot and restarts on failure.

**Check status (+ journal on error):**
```bash
../scripts/backendStatus.sh --host 192.168.2.149
```
- Verifies the DLL is present on the Pi
- Prints `systemctl status`
- On failure, dumps the last 40 lines of `journalctl`

**Remove:**
```bash
../scripts/backendRemove.sh --host 192.168.2.149
```

The backend listens on **port 8080** — access it at `http://<pi-ip>:8080`.

## macOS Temperature Monitoring

For extended temperature sensor data on macOS, install [Stats.app](https://github.com/exelban/stats):

```bash
brew install stats
```

The application will automatically detect and use the SMC binary from Stats.app to read CPU core, GPU, SSD, memory, and airflow temperatures.

## Technical Notes

- HTTP/1.1, listens on all interfaces (`0.0.0.0` / `[::]`) port 8080
- I2C auto-detection runs at startup; virtual sensors are registered automatically
- Background sensor threads run at the native sensor frame rate; `ReadOnce()` returns the latest cached result
- Enum serialization using `JsonStringEnumConverter`

---

GMA, 2026-03-18
