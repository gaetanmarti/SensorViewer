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
};

/**
 * API endpoints configuration
 */
export const API_ENDPOINTS = {
  // Health check
  ALIVE: '/api/alive',
  
  // Sensors
  SENSORS: '/api/sensors',
};
