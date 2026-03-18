# Webserver for real-time sensors - API Documentation

## Overview

This REST API provides real-time sensor data streaming. The server runs on port 8080 and includes both HTTP endpoints for the front-end, as well as Web-server functionality.

## Base URL

In this documentation, the base URL is defined as follows:

```
http://localhost:8080
```

---

### General Endpoints

---

#### `GET /api/alive`

Health check endpoint to verify if the server is running and available.

##### Response (200 OK):

```json
{
  "ok": true
}
```

##### Error responses:

- `500`: Internal server error

##### Example:

```bash
curl -X GET http://localhost:8080/api/alive

{"ok":true}
```

---

#### `GET /api/sensors`

Get sensors values

##### Response (200 OK):

```json
{
  "sensors": [
    { "name": "CPU Usage", "unit": "Percent", "value": "6.11" },
    { "name": "RAM Usage", "unit": "Percent", "value": "65.54" },
    ...
    { "name": "Intel GPU", "unit": "Temperature", "value": "59.00" },
  ]
}
```

Return the sensor list with:
- Name of the sensor,
- Unit [None, Temperature, Percent]
- Value

##### Error responses:

- `500`: Internal server error

##### Example:

```bash
 curl -X GET http://localhost:8080/api/sensors

 {"sensors":[...]}
```

---

### I2C Endpoints

---

#### `GET /api/i2c/devices`

Detect and list all I2C devices connected on the bus.

##### Response (200 OK):

```json
{
  "ok": true,
  "devices": [
    {
      "address": 65,
      "name": "TMF882X Time-of-Flight Sensor",
      "type": "Distance"
    },
    {
      "address": 105,
      "name": "AMG8833 Thermal Camera (Grid-EYE)",
      "type": "Thermal"
    }
  ]
}   
```

Return the list of detected I2C devices with:
- **Address**: I2C device address (decimal format)
- **Name**: Device name (if registered) or "Unknown I2C Device"
- **Type**: Device type. Current supported types:
  - `"Unknown"` - Unrecognized device
  - `"Distance"` - Distance/ToF sensors (VL53L5CX, TMF882X)
  - `"Thermal"` - Thermal/infrared cameras (AMG8833, MLX90640)
  - `"HumanPresence"` - Human presence and motion sensors (STHS34PF80, ThermalHumanPresenceSensor)
  - `"HumanDistance"` - Virtual human detection sensor derived from a distance sensor (DistanceHumanPresenceSensor)
  - `"Environmental"` - Environmental sensors: temperature, humidity, pressure, gas (BME680)

> **Virtual sensors**: When a thermal or distance sensor is detected, a corresponding virtual sensor is automatically created and also returned in this list:
> - `ThermalHumanPresenceSensor` — created from any `II2CThermalSensor` (address = sensor address + `0x80`)
> - `DistanceHumanPresenceSensor` — created from any `II2CDistanceSensor` (address = sensor address + `0x80`)

##### Error responses:

- `500`: Internal server error
- `499`: Operation cancelled

##### Example:

```bash
curl -X GET http://localhost:8080/api/i2c/devices

# Example response with multiple device types:
{"ok":true,"devices":[
  {"address":65,"name":"TMF882X Time-of-Flight Sensor","type":"Distance"},
  {"address":90,"name":"STHS34PF80","type":"HumanPresence"},
  {"address":105,"name":"AMG8833 Thermal Camera (Grid-EYE)","type":"Thermal"},
  {"address":193,"name":"Human Distance Sensor (Virtual from TMF882X Time-of-Flight Sensor)","type":"HumanDistance"}
]}
```

---

#### `GET /api/i2c/device/{address}/specifications`

Return the specification record for a sensor at the given I2C address (works with distance, thermal, and human presence/distance sensors).

##### Parameters:

- `address`: I2C address in decimal or hexadecimal (e.g. `65` or `0x41`).

##### Response (200 OK) - Distance Sensor:

```json
{
  "ok": true,
  "address": 65,
  "name": "TMF882X Time-of-Flight Sensor",
  "type": "Distance",
  "specifications": {
    "width": 3,
    "height": 3,
    "updateRateHz": 30,
    "verticalFOVDeg": 33,
    "horizontalFOVDeg": 32
  }
}
```

##### Response (200 OK) - Virtual Human Distance Sensor:

```json
{
  "ok": true,
  "address": 193,
  "name": "Human Distance Sensor (Virtual from TMF882X Time-of-Flight Sensor)",
  "type": "HumanDistance",
  "specifications": {
    "updateRateHz": 30,
    "verticalFOVDeg": 33,
    "horizontalFOVDeg": 32,
    "maxRangeMeters": 5.0
  }
}
```

Note: The virtual sensor address is the underlying distance sensor address + `0x80` (e.g. TMF882X at `0x41` → virtual at `0xC1` = 193).

##### Response (200 OK) - Thermal Sensor:

```json
{
  "ok": true,
  "address": 105,
  "name": "AMG8833 Thermal Camera (Grid-EYE)",
  "type": "Thermal",
  "specifications": {
    "width": 8,
    "height": 8,
    "updateRateHz": 10,
    "verticalFOVDeg": 60,
    "horizontalFOVDeg": 60,
    "minTempCelsius": -20,
    "maxTempCelsius": 80,
    "resolutionCelsius": 0.25
  }
}
```

##### Response (200 OK) - Human Presence Sensor:

```json
{
  "ok": true,
  "address": 90,
  "name": "STHS34PF80",
  "type": "HumanPresence",
  "specifications": {
    "updateRateHz": 4,
    "verticalFOVDeg": 80,
    "horizontalFOVDeg": 80,
    "minTempCelsius": -10,
    "maxTempCelsius": 60,
    "resolutionCelsius": 0.01,
    "detectionRangeMeters": 4
  }
}
```

##### Response (200 OK) - Environmental Sensor:

```json
{
  "ok": true,
  "address": 119,
  "name": "BME680",
  "type": "Environmental",
  "specifications": {
    "hasTemperature": true,
    "hasHumidity": true,
    "hasPressure": true,
    "hasGas": true
  }
}
```

##### Error responses:

- `400`: Invalid I2C address
- `404`: Device not found
- `500`: Internal server error
- `499`: Operation cancelled

##### Examples:

```bash
# Distance sensor
curl -X GET http://localhost:8080/api/i2c/device/0x41/specifications

# Virtual human distance sensor (TMF882X at 0x41 → virtual at 0xC1)
curl -X GET http://localhost:8080/api/i2c/device/0xC1/specifications

# Thermal sensor
curl -X GET http://localhost:8080/api/i2c/device/0x69/specifications

# Human presence sensor
curl -X GET http://localhost:8080/api/i2c/device/0x5A/specifications

# Environmental sensor
curl -X GET http://localhost:8080/api/i2c/device/0x77/specifications
```

---

#### `GET /api/i2c/device/{address}/data`

Return a single measurement from the specified sensor (works with distance, thermal, and human presence/distance sensors).

##### Parameters:

- `address`: I2C address in decimal or hexadecimal (e.g. `65` or `0x41`).

##### Response (200 OK) - Distance Sensor:

```json
{
  "ok": true,
  "address": 65,
  "name": "TMF882X Time-of-Flight Sensor",
  "type": "Distance",
  "measurement": [
    { "distMM": 482, "confidence": 0.95 },
    { "distMM": 490, "confidence": 0.92 }
  ]
}
```

##### Response (200 OK) - Virtual Human Distance Sensor:

```json
{
  "ok": true,
  "address": 193,
  "name": "Human Distance Sensor (Virtual from TMF882X Time-of-Flight Sensor)",
  "type": "HumanDistance",
  "measurement": {
    "presence": true,
    "position": {
      "x": -0.042,
      "y": 0.015,
      "z": 1.204,
      "distance": 1.208
    },
    "quality01": 0.871
  }
}
```

Note: For virtual human distance sensors, the measurement includes:
- `presence`: Boolean — true if a human is detected in the field of view
- `position`: 3-D coordinates in **metres** relative to the sensor origin (Z = forward axis, X = right, Y = up), or `null` when no human is detected
  - `x`, `y`, `z`: Cartesian components
  - `distance`: Euclidean distance $\sqrt{x^2 + y^2 + z^2}$ in metres
- `quality01`: Detection confidence in `[0, 1]` (0 = no confidence, 1 = maximum confidence)

The virtual sensor address is the underlying distance sensor address + `0x80` (e.g. TMF882X at `0x41` → virtual at `0xC1` = 193).

##### Response (200 OK) - Thermal Sensor:

```json
{
  "ok": true,
  "address": 105,
  "name": "AMG8833 Thermal Camera (Grid-EYE)",
  "type": "Thermal",
  "measurement": {
    "temperatures": [
      [22.5, 22.75, 23.0, 23.25, 23.5, 23.75, 24.0, 24.25],
      [22.25, 22.5, 22.75, 23.0, 23.25, 23.5, 23.75, 24.0],
      [22.0, 22.25, 22.5, 22.75, 23.0, 23.25, 23.5, 23.75],
      [21.75, 22.0, 22.25, 22.5, 22.75, 23.0, 23.25, 23.5],
      [21.5, 21.75, 22.0, 22.25, 22.5, 22.75, 23.0, 23.25],
      [21.25, 21.5, 21.75, 22.0, 22.25, 22.5, 22.75, 23.0],
      [21.0, 21.25, 21.5, 21.75, 22.0, 22.25, 22.5, 22.75],
      [20.75, 21.0, 21.25, 21.5, 21.75, 22.0, 22.25, 22.5]
    ]
  }
}
```

Note: For thermal sensors, `temperatures` is a 2D array where each element represents the temperature in Celsius at that pixel location. Array dimensions match the sensor specifications (e.g., 8x8 for AMG8833).

##### Response (200 OK) - Human Presence Sensor:

```json
{
  "ok": true,
  "address": 90,
  "name": "STHS34PF80",
  "type": "HumanPresence",
  "measurement": {
    "presenceDetected": true,
    "motionDetected": false,
    "ambientShockDetected": false,
    "ambientTemperatureCelsius": 23.45,
    "objectTemperatureCelsius": 33.21,
    "presenceValue": 1250,
    "motionValue": 45,
    "ambientShockValue": 12
  }
}
```

Note: For human presence sensors, the measurement includes:
- `presenceDetected`: Boolean indicating if human presence is detected (true when presenceValue exceeds the configured threshold)
- `motionDetected`: Boolean indicating if motion is detected (true when motionValue exceeds the configured threshold)
- `ambientShockDetected`: Boolean indicating if ambient temperature shock is detected (true when ambientShockValue exceeds the configured threshold)
- `ambientTemperatureCelsius`: Ambient temperature measurement
- `objectTemperatureCelsius`: Absolute object (human) temperature measurement in Celsius
- `presenceValue`: Raw presence signature value in 0.01°C units (e.g., 200 = 2.00°C)
- `motionValue`: Raw motion signature value in 0.01°C units
- `ambientShockValue`: Raw ambient shock value in 0.01°C units

**Detection Configuration:**
The STHS34PF80 sensor uses configurable thresholds and hysteresis to determine when to set the detection flags.

*Thresholds* (values in 0.01°C units):
- `presenceThreshold`: Default 150 (1.50°C) - Minimum thermal signature to detect human presence
- `motionThreshold`: Default 150 (1.50°C) - Minimum thermal signature change to detect motion
- `ambientShockThreshold`: Default 200 (2.00°C) - Minimum ambient temperature change to detect shock

*Hysteresis* (prevents false oscillations, values in 0.01°C units):
- `presenceHysteresis`: Default 50 (0.50°C) - Once detected, presence must drop below (threshold - hysteresis) to clear
- `motionHysteresis`: Default 50 (0.50°C) - Once detected, motion must drop below (threshold - hysteresis) to clear
- `ambientShockHysteresis`: Default 50 (0.50°C) - Once detected, shock must drop below (threshold - hysteresis) to clear

These parameters can be configured during sensor initialization via the `config` dictionary.
The raw values (`presenceValue`, `motionValue`, `ambientShockValue`) are always available regardless of threshold configuration.

##### Response (200 OK) - Environmental Sensor:

```json
{
  "ok": true,
  "address": 119,
  "name": "BME680",
  "type": "Environmental",
  "measurement": {
    "temperatureCelsius": 24.35,
    "humidityPercent": 47.82,
    "pressureHPa": 1013.25,
    "gasResistanceOhms": 51423,
    "iaqIndex": 42.5
  }
}
```

Note: For environmental sensors, the measurement includes:
- `temperatureCelsius`: Ambient temperature in °C
- `humidityPercent`: Relative humidity in %
- `pressureHPa`: Barometric pressure in hPa
- `gasResistanceOhms`: Gas resistance in Ohms (VOC indicator — higher value = cleaner air)
- `iaqIndex`: Indoor Air Quality index on the **US AQI scale (0–500, lower = better)**, computed from gas resistance and humidity (David Bird / G6EJD algorithm, 75 % gas + 25 % humidity contribution):

  | IAQ range | Category |
  |-----------|----------|
  | 0 – 50    | Good |
  | 51 – 150  | Moderate |
  | 151 – 175 | Unhealthy for Sensitive Groups |
  | 176 – 200 | Unhealthy |
  | 201 – 300 | Very Unhealthy |
  | 301 – 500 | Hazardous |

Fields are `null` if the sensor does not support that measurement type.

##### Error responses:

- `400`: Invalid I2C address
- `404`: Device not found
- `408`: Measurement timeout
- `500`: Internal server error
- `499`: Operation cancelled

##### Examples:

```bash
# Distance sensor
curl -X GET http://localhost:8080/api/i2c/device/0x41/data

# Virtual human distance sensor (TMF882X at 0x41 → virtual at 0xC1)
curl -X GET http://localhost:8080/api/i2c/device/0xC1/data

# Thermal sensor
curl -X GET http://localhost:8080/api/i2c/device/0x69/data

# Human presence sensor
curl -X GET http://localhost:8080/api/i2c/device/0x5A/data

# Environmental sensor
curl -X GET http://localhost:8080/api/i2c/device/0x77/data
```

---

### Sensor Configuration Notes

---

#### TMF882X — Range Mode (`shortRange`)

The TMF882X supports two range accuracy modes (§4.2 of the AMS host driver communication note). The mode is selected at initialization time via the `shortRange` config key:

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `shortRange` | `bool` | `false` | `true` = short-range accuracy mode (improved precision at close range); `false` = long-range mode (default at firmware startup) |
| `periodMs` | `ushort` | `34` | Measurement period in milliseconds |
| `kiloIterations` | `ushort` | `550` | Measurement iterations × 1024 |

The mode is only applied if the firmware reports support (bit 4 of `BUILD_VERSION` register `0x03`). The firmware always starts up in **long-range** mode.

---

GMA 2026-03-18
