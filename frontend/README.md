# SensorViewer Web Frontend

A simple web interface for monitoring PC and i2c sensors in real-time, built with **Svelte + Vite + TailwindCSS**.

## Tech Stack

- **Framework**: Svelte 5
- **Build Tool**: Vite 6
- **Styling**: TailwindCSS 3
- **Language**: JavaScript

## Features

- **No Authentication**: Direct access to sensor monitoring
- **Tab-Based Navigation**: Switch between PC Sensors and i2c Sensors
- **Real-Time Updates**: Automatic refresh every 5 seconds
- **Responsive Design**: Adapts to mobile, tablet, and desktop screens
- **Color-Coded Values**: Visual indicators for temperature and usage levels

## Project Structure

```
src/
├── routes/          # Page components
│   ├── PCSensors.svelte   # PC sensors monitoring with live updates
│   └── I2CSensors.svelte  # i2c sensors (placeholder)
├── lib/             # Utilities and configuration
│   └── config.js          # API configuration and constants
├── App.svelte       # Main application with tab navigation
```

## API Integration

The frontend connects to the backend API running on `http://localhost:8080`:
- `/api/alive`: Health check endpoint
- `/api/sensors`: Get all sensors data (refreshed every 5 seconds)

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

### Polling Interval
The PC Sensors refresh interval can be adjusted in `src/lib/config.js`:
```javascript
export const POLLING_INTERVALS = {
  PC_SENSORS: 5000, // milliseconds
};
```

## Color Coding

Sensor values are color-coded based on their type and value:

### Temperature
- 🟢 Green: < 60°C (normal)
- 🟠 Orange: 60-79°C (warning)
- 🔴 Red: ≥ 80°C (danger)

### Percentage (CPU/RAM Usage)
- 🟢 Green: < 70% (normal)
- 🟠 Orange: 70-89% (warning)
- 🔴 Red: ≥ 90% (danger)

---

GMA, 2026-01-28