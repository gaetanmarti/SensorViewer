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
  - `"Thermal"` - Thermal/infrared cameras (AMG8833)

##### Error responses:

- `500`: Internal server error
- `499`: Operation cancelled

##### Example:

```bash
curl -X GET http://localhost:8080/api/i2c/devices

# Example response with multiple device types:
{"ok":true,"devices":[
  {"address":65,"name":"TMF882X Time-of-Flight Sensor","type":"Distance"},
  {"address":105,"name":"AMG8833 Thermal Camera (Grid-EYE)","type":"Thermal"}
]}
```

---

#### `GET /api/i2c/device/{address}/specifications`

Return the specification record for a sensor at the given I2C address (works with distance and thermal sensors).

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
```

---

#### `GET /api/i2c/device/{address}/measure`

Return a single measurement from the specified sensor (works with distance and thermal sensors).

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

##### Error responses:

- `400`: Invalid I2C address
- `404`: Device not found
- `408`: Measurement timeout
- `500`: Internal server error
- `499`: Operation cancelled

##### Examples:

```bash
# Distance sensor
curl -X GET http://localhost:8080/api/i2c/device/0x41/measure

# Thermal sensor
curl -X GET http://localhost:8080/api/i2c/device/0x69/measure
```

---

GMA 2026-02-04
