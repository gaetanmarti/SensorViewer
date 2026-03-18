# SensorViewer Web Frontend

A simple web interface for monitoring PC and I2C sensors in real-time, built with **Svelte + Vite + TailwindCSS**.

## Tech Stack

- **Framework**: Svelte 5
- **Build Tool**: Vite 6
- **Styling**: TailwindCSS 3
- **Language**: JavaScript

## Features

- **No Authentication**: Direct access to sensor monitoring
- **Tab-Based Navigation**: Switch between PC Sensors and I2C Sensors
- **Real-Time Updates**: Automatic polling at type-specific intervals (see [Polling Intervals](#polling-intervals))
- **Responsive Design**: Adapts to mobile, tablet, and desktop screens
- **Color-Coded Values**: Visual indicators for temperature, usage levels, and sensor confidence
- **I2C Device Discovery**: Automatic scanning and detection of I2C devices on the bus
- **Dynamic Widgets**: Type-specific sensor widgets rendered automatically based on device type

## Project Structure

```
src/
├── routes/          # Page components
│   ├── PCSensors.svelte        # PC sensors monitoring with live updates
│   └── I2CSensors.svelte       # I2C sensors with device discovery and live measurements
├── components/      # Reusable UI components
│   ├── SensorCard.svelte           # PC sensor display card with history graph
│   ├── UnknownSensor.svelte        # Widget for unrecognized I2C devices
│   ├── DistanceSensor.svelte       # Widget for Time-of-Flight distance sensors (grid + 3D view)
│   ├── ThermalSensor.svelte        # Widget for thermal/infrared cameras (heatmap)
│   ├── HumanPresenceSensor.svelte  # Widget for human presence/motion sensors (STHS34PF80, …)
│   ├── HumanDistanceSensor.svelte  # Widget for virtual human distance sensors (DistanceHumanPresenceSensor)
│   └── EnvironmentalSensor.svelte  # Widget for environmental sensors (BME680: temp, humidity, pressure, gas)
├── lib/             # Utilities and configuration
│   └── config.js          # API configuration and constants
├── App.svelte       # Main application with tab navigation
```

## I2C Sensor Types

| Type | Component | Description |
|---|---|---|
| `Distance` | `DistanceSensor` | Time-of-Flight sensors — live distance grid and 3D point cloud (VL53L5CX, TMF882X) |
| `Thermal` | `ThermalSensor` | Thermal/infrared cameras — live heatmap (AMG8833, MLX90640) |
| `HumanPresence` | `HumanPresenceSensor` | Human presence and motion sensors — presence/motion/shock flags + temperature (STHS34PF80) |
| `HumanDistance` | `HumanDistanceSensor` | Virtual human detection derived from a ToF sensor — 3D position + quality (DistanceHumanPresenceSensor) |
| `Environmental` | `EnvironmentalSensor` | Environmental sensors — temperature, humidity, pressure, gas (BME680) |
| `Unknown` | `UnknownSensor` | Unrecognized or unsupported devices |

> Virtual sensors (`HumanDistance` and the thermal equivalent) are created automatically by the backend when a compatible distance or thermal sensor is detected. Their I2C address is the underlying sensor address + `0x80`.

## API Integration

The frontend connects to the backend API running on `http://localhost:8080`.

See the [backend API documentation](../backend/API.md) for complete API details.

## Development

### Prerequisites
- Node.js 18+
- npm or yarn

### Install Dependencies
```bash
npm install
# or
yarn install
```

### Start Development Server
```bash
npm run dev
# or
yarn dev
```

The application will be available at `http://localhost:5173` (or next available port).

### Build for Production
```bash
npm run build
# or
yarn build
```

### Build and Deploy to Backend
Builds the frontend and copies the output to the backend's `webapp` folder in one step:
```bash
npm run build:backend
```

## Configuration

### API Base URL
In development the Vite proxy forwards API calls to `http://localhost:8080` automatically.  
In production, set the `VITE_API_BASE_URL` environment variable (see `.env`).

### Polling Intervals
Refresh intervals for each sensor type can be adjusted in `src/lib/config.js`:

```javascript
export const POLLING_INTERVALS = {
  PC_SENSORS: 5000,                  // PC sensors (5 s)
  I2C_DISTANCE_SENSORS: 100,         // ToF distance sensors (100 ms)
  I2C_THERMAL_SENSORS: 250,          // Thermal cameras (250 ms)
  I2C_HUMAN_PRESENCE_SENSORS: 250,   // Human presence sensors (250 ms)
  I2C_HUMAN_DISTANCE_SENSORS: 250,   // Virtual human distance sensors (250 ms)
  I2C_ENVIRONMENTAL_SENSORS: 1000,   // Environmental sensors (1 s)
};
```

## Color Coding

Sensor values are color-coded based on their type and value:

### Temperature (PC Sensors)
- 🟢 Green: < 60°C (normal)
- 🟠 Orange: 60-79°C (warning)
- 🔴 Red: ≥ 80°C (danger)

### Percentage (CPU/RAM Usage)
- 🟢 Green: < 70% (normal)
- 🟠 Orange: 70-89% (warning)
- 🔴 Red: ≥ 90% (danger)

### Distance Sensor Confidence (I2C Sensors)
- 🟢 Green: ≥ 80% confidence (high accuracy)
- 🟡 Yellow: 50-79% confidence (medium accuracy)
- 🔴 Red: < 50% confidence (low accuracy)

## I2C Sensors

### Supported Device Types

#### Distance Sensors (Time-of-Flight)
Displays live distance measurements with:
- Multi-zone grid layout (e.g., 3x3 grid for TMF882X)
- Distance values in mm or m
- Confidence levels with color coding
- Sensor specifications (FOV, update rate, grid size)
- Configurable polling interval (default: 1 second)

#### Unknown Devices
For unrecognized or unsupported I2C devices, displays:
- Device name and I2C address (hex and decimal)
- Device type
- Placeholder message indicating no widget is available yet

### Adding New Sensor Types

To add support for new I2C sensor types:

1. Create a new component in `src/components/` (e.g., `TemperatureSensor.svelte`)
2. Implement the sensor widget with appropriate API calls
3. Update the `getComponentForDevice()` function in `src/routes/I2CSensors.svelte`:
   ```javascript
   function getComponentForDevice(type) {
     switch (type?.toLowerCase()) {
       case 'distance':
         return DistanceSensor;
       case 'temperature':  // Add new type
         return TemperatureSensor;
       default:
         return UnknownSensor;
     }
   }
   ```

---

GMA, 2026-01-28  
Updated: 2026-02-09