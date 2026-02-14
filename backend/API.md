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
  - `"HumanPresence"` - Human presence and motion sensors (STHS34PF80)

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
  {"address":105,"name":"AMG8833 Thermal Camera (Grid-EYE)","type":"Thermal"}
]}
```

---

#### `GET /api/i2c/device/{address}/specifications`

Return the specification record for a sensor at the given I2C address (works with distance, thermal, and human presence sensors).

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

##### Error responses:

- `400`: Invalid I2C address
- `404`: Device not found
- `500`: Internal server error
- `499`: Operation cancelled

##### Examples:

```bash
# Distance sensor
curl -X GET http://localhost:8080/api/i2c/device/0x41/specifications

# Thermal sensor
curl -X GET http://localhost:8080/api/i2c/device/0x69/specifications

# Human presence sensor
curl -X GET http://localhost:8080/api/i2c/device/0x5A/specifications
```

---

#### `GET /api/i2c/device/{address}/data`

Return a single measurement from the specified sensor (works with distance, thermal, and human presence sensors).

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

**Detection Thresholds:**
The STHS34PF80 sensor uses configurable thresholds to determine when to set the detection flags:
- `presenceThreshold`: Default 200 (2.00°C) - Minimum thermal signature to detect human presence
- `motionThreshold`: Default 200 (2.00°C) - Minimum thermal signature change to detect motion
- `ambientShockThreshold`: Default 200 (2.00°C) - Minimum ambient temperature change to detect shock

These thresholds can be configured during sensor initialization via the `config` dictionary.
The raw values (`presenceValue`, `motionValue`, `ambientShockValue`) are always available regardless of threshold configuration.

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

# Thermal sensor
curl -X GET http://localhost:8080/api/i2c/device/0x69/data

# Human presence sensor
curl -X GET http://localhost:8080/api/i2c/device/0x5A/data
```

---

GMA 2026-02-04
