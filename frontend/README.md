# SensorViewer Web Frontend

A simple web interface for monitoring PC and i2c sensors in real-time, built with **Svelte + Vite + TailwindCSS**.

## Tech Stack

- **Framework**: Svelte 5
- **Build Tool**: Vite 6
- **Styling**: TailwindCSS 3v
- **Language**: JavaScript

## Features

- **No Authentication**: Direct access to sensor monitoring
- **Tab-Based Navigation**: Switch between PC Sensors and I2C Sensors
- **Real-Time Updates**: Automatic refresh (PC sensors: 5s, I2C distance sensors: 1s)
- **Responsive Design**: Adapts to mobile, tablet, and desktop screens
- **Color-Coded Values**: Visual indicators for temperature, usage levels, and sensor confidence
- **I2C Device Discovery**: Automatic scanning and detection of I2C devices
- **Dynamic Widgets**: Type-specific sensor widgets (Distance sensors, Unknown devices)

## Project Structure

```
src/
├── routes/          # Page components
│   ├── PCSensors.svelte   # PC sensors monitoring with live updates
│   └── I2CSensors.svelte  # I2C sensors with device discovery and live measurements
├── components/      # Reusable UI components
│   ├── SensorCard.svelte      # PC sensor display card
│   ├── UnknownSensor.svelte   # Widget for unrecognized I2C devices
│   └── DistanceSensor.svelte  # Widget for Time-of-Flight distance sensors
├── lib/             # Utilities and configuration
│   └── config.js          # API configuration and constants
├── App.svelte       # Main application with tab navigation
```

## API Integration

The frontend connects to the backend API running on `http://localhost:8080`:

### PC Sensors
- `/api/alive`: Health check endpoint
- `/api/sensors`: Get all PC sensors data (refreshed every 5 seconds)

### I2C Sensors
- `/api/i2c/devices`: Scan and list all I2C devices on the bus
- `/api/i2c/device/{address}/specifications`: Get device specifications (grid size, FOV, update rate)
- `/api/i2c/device/{address}/measure`: Get live distance measurements (refreshed every 1 second)

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

## Configuration

### API Base URL
Edit `src/lib/config.js` to change the backend API URL:
```javascript
export const API_BASE_URL = 'http://localhost:8080';
```

### Polling Intervals
Refresh intervals for different sensor types can be adjusted in `src/lib/config.js`:
```javascript
export const POLLING_INTERVALS = {
  PC_SENSORS: 5000,              // PC sensors (5 seconds)
  I2C_DISTANCE_SENSORS: 1000,    // I2C distance sensors (1 second)
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