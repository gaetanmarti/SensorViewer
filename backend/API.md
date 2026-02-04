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
    { "address": 60, "name": "Unknown I2C Device" },
    { "address": 72, "name": "Custom Sensor" }
  ]
}
```

Return the list of detected I2C devices with:
- Address: I2C device address (decimal format)
- Name: Device name (if registered) or "Unknown I2C Device"

##### Example:

```bash
curl -X GET http://localhost:8080/api/i2c/devices

{"ok":true,"devices":[{"address":60,"name":"Unknown I2C Device"}]}
```

---

GMA 2026-02-04
