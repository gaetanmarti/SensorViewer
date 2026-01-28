# SensorViewer Design System

This document describes the design system for the SensorViewer web interface.

## Overview

SensorViewer is a simple, clean interface for monitoring sensors in real-time. It features:
- No authentication required
- Tab-based navigation (PC Sensors, i2c Sensors)
- Real-time data updates every 5 seconds
- Responsive grid layout

## Color Palette

### Primary Colors
- **Primary Blue**: `#3B82F6` (blue-500) - Used for active tabs
- **Primary Blue Hover**: `#2563EB` (blue-600)

### Status Colors
- **Temperature Warning** (Orange): `#F97316` (orange-500) - Temp >= 60°C
- **Temperature Danger** (Red): `#DC2626` (red-600) - Temp >= 80°C
- **Percentage Warning** (Orange): `#F97316` (orange-500) - Usage >= 70%
- **Percentage Danger** (Red): `#DC2626` (red-600) - Usage >= 90%
- **Success** (Green): `#16A34A` (green-600) - Normal values

### Neutral Colors
Uses Tailwind CSS gray shades (gray-50 to gray-900)

## Layout Structure

### Header
- White background with shadow
- Contains application title "Sensor Viewer"
- Fixed container with max-width and padding

### Navigation Tabs
- White background with bottom border
- Horizontal tab bar
- Active tab: blue border-bottom and blue text
- Inactive tabs: transparent border with gray text and hover effects

### Main Content Area
- Light gray background (`bg-gray-50`)
- Responsive grid layout for sensor cards
- 1 column on mobile, 2 on tablet, 3 on desktop

### Sensor Cards
- White background with rounded corners
- Subtle shadow with hover effect
- Border for definition
- Sensor name in smaller gray text
- Large value display with color-coded status
- Unit symbol displayed alongside value

## Configuration

### API Configuration
Configuration is centralized in `/src/lib/config.js`:
```javascript
export const API_BASE_URL = 'http://localhost:8080';
export const POLLING_INTERVALS = {
  PC_SENSORS: 5000, // 5 seconds
};
```

## Components

### PCSensors (`/src/routes/PCSensors.svelte`)
Main component that:
- Fetches sensor data from `/api/sensors`
- Refreshes every 5 seconds
- Displays sensors in a responsive grid
- Shows loading spinner during initial load
- Displays error messages on connection issues
- Color-codes values based on thresholds

### I2CSensors (`/src/routes/I2CSensors.svelte`)
Placeholder component for future i2c sensor implementation.

## Responsive Design

The interface adapts to different screen sizes:
- **Mobile** (< 768px): 1 column grid
- **Tablet** (768px - 1024px): 2 column grid
- **Desktop** (>= 1024px): 3 column grid

## Dependencies

- **Svelte**: Component framework
- **Tailwind CSS**: Utility-first CSS framework
- **Vite**: Build tool and dev server
- `onclick`: `function` (optional)
- `class`: `string` (optional, to add custom classes)

**Usage Examples:**

```svelte
<!-- Primary button (default) -->
<Button onclick={handleClick}>
  Click Me
</Button>

<!-- Submit button with full width -->
<Button type="submit" variant="primary" fullWidth={true}>
  Sign in
</Button>

<!-- Ghost button (border only) -->
<Button variant="ghost" onclick={handleReset}>
  Reset
</Button>

<!-- Small danger button -->
<Button variant="danger" size="sm" onclick={handleDelete}>
  Delete
</Button>

<!-- Disabled button -->
<Button disabled={loading}>
  {#if loading}
    <span class="loading loading-spinner"></span>
    Loading...
  {:else}
    Save
  {/if}
</Button>
```

## Status Mapping

Device states are mapped to colors in `src/lib/colors.js`:

| Status Code | State | Color | Description |
|-------------|-------|-------|-------------|
| 0 | GetStatus | Orange | Service code |
| 1 | Stopped | Red | Complete stop |
| 2 | TankFull | Green | Tank full (paused) |
| 3 | CheckPressure | Orange | Low pressure (paused) |
| 4 | CheckPower | Red | Power supply issue |
| 5 | WaitCurrent | Orange | Waiting for current |
| 6 | Working | Green | Normal operation |
| 7 | Softener | Orange | Softener regeneration (paused) |
| 8 | PPMsChanged | Orange | Generating new solution |

## Design Principles

1. **Simplicity**: Use only Aquama blue and status colors (red, orange, green)
2. **Consistency**: All buttons must use the `Button` component
3. **Accessibility**: Colors meet WCAG AA contrast standards
4. **Responsiveness**: Use Tailwind utilities for responsive design

## Migration

To update an existing button to the new system:

**Before:**
```svelte
<button class="btn btn-primary" on:click={handleClick}>
  Click Me
</button>
```

**After:**
```svelte
<Button variant="primary" onclick={handleClick}>
  Click Me
</Button>
```

