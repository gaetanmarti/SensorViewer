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
  "ok":true,
  "devices":
  [
    {"address":65,"name":"TMF882X Time-of-Flight Sensor","type":"Distance"}
  ]
}   
```

Return the list of detected I2C devices with:
- Address: I2C device address (decimal format)
- Name: Device name (if registered) or "Unknown I2C Device"
- Type: Device type (e.g. Distance)

##### Error responses:

- `500`: Internal server error
- `499`: Operation cancelled

##### Example:

```bash
curl -X GET http://localhost:8080/api/i2c/devices

{"ok":true,"devices":[{"address":65,"name":"TMF882X Time-of-Flight Sensor","type":"TMF882X"}]}
```

---

#### `GET /api/i2c/device/{address}/specifications`

Return the specification record for a distance sensor at the given I2C address.

##### Parameters:

- `address`: I2C address in decimal or hexadecimal (e.g. `65` or `0x41`).

##### Response (200 OK):

```json
{
  "ok": true,
  "address": 65,
  "name": "TMF882X Time-of-Flight Sensor",
  "specifications": {
    "width": 3,
    "height": 3,
    "updateRateHz": 30,
    "verticalFOVDeg": 33,
    "horizontalFOVDeg": 32
  }
}
```

##### Error responses:

- `400`: Invalid I2C address
- `404`: Distance sensor not found
- `500`: Internal server error
- `499`: Operation cancelled

##### Example:

```bash
curl -X GET http://localhost:8080/api/i2c/device/0x41/specifications
```

---

#### `GET /api/i2c/device/{address}/measure`

Return a single distance measurement for the specified distance sensor.

##### Parameters:

- `address`: I2C address in decimal or hexadecimal (e.g. `65` or `0x41`).

##### Response (200 OK):

```json
{
  "ok": true,
  "address": 65,
  "name": "TMF882X Time-of-Flight Sensor",
  "measurement": [
    { "distMM": 482, "confidence": 0.95 },
    { "distMM": 490, "confidence": 0.92 }
  ]
}
```

##### Error responses:

- `400`: Invalid I2C address
- `404`: Distance sensor not found
- `408`: Measurement timeout
- `500`: Internal server error
- `499`: Operation cancelled

##### Example:

```bash
curl -X GET http://localhost:8080/api/i2c/device/0x41/measure
```

---

GMA 2026-02-04
