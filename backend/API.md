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

GMA 2026-01-28
