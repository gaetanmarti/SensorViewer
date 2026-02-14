/**
 * SensorViewer Application Configuration
 * 
 * This file centralizes all application-wide configuration constants.
 */

/**
 * API Base URL
 * In development, use empty string to leverage Vite's proxy
 * In production, this would be the full backend URL
 */
export const API_BASE_URL = import.meta.env.MODE === 'production' ? 'http://192.168.4.50:8080' : '';

/**
 * API polling intervals (in milliseconds)
 */
export const POLLING_INTERVALS = {
  // PC Sensors polling interval (5000ms = every 5 seconds)
  PC_SENSORS: 5000,
  
  // I2C distance sensors polling interval (300ms)
  I2C_DISTANCE_SENSORS: 300,
  
  // I2C thermal sensors polling interval (1000ms = every 1 second)
  I2C_THERMAL_SENSORS: 250,
  
  // I2C human presence sensors polling interval (250ms)
  I2C_HUMAN_PRESENCE_SENSORS: 250,
};

/**
 * Thermal sensor configuration
 */
export const THERMAL_CONFIG = {
  // Temperature scale margin in Celsius (added/subtracted to min/max for stable display)
  TEMP_SCALE_MARGIN: 2.0,
  
  // Smoothing factor for temperature scale (0.0 = no change, 1.0 = instant change)
  // Lower values = slower, smoother transitions
  TEMP_SCALE_SMOOTHING: 0.15,
  
  // Threshold for radical temperature change (in Celsius)
  // If temperature changes by more than this, skip smoothing and update immediately
  TEMP_RADICAL_CHANGE_THRESHOLD: 5.0,
};

/**
 * API endpoints configuration
 */
export const API_ENDPOINTS = {
  // Health check
  ALIVE: '/api/alive',
  
  // Sensors
  SENSORS: '/api/sensors',
  
  // I2C endpoints
  I2C_DEVICES: '/api/i2c/devices',
  I2C_DEVICE_SPECIFICATIONS: (address) => `/api/i2c/device/${address}/specifications`,
  I2C_DEVICE_MEASURE: (address) => `/api/i2c/device/${address}/data`,
};
