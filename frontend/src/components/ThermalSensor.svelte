<script>
  /**
   * ThermalSensor Component
   * 
   * Displays real-time thermal measurements from I2C thermal cameras (e.g., AMG8833 Grid-EYE).
   * Automatically polls the sensor at a configurable interval and displays:
   * - Device information (name, address, specifications)
   * - Live thermal measurements in an interpolated heatmap view
   * - Temperature range and statistics
   * 
   * @component
   * @prop {Object} device - The I2C thermal sensor device object
   * @prop {number} device.address - I2C address in decimal format
   * @prop {string} device.name - Device name
   * @prop {string} device.type - Device type (should be "Thermal")
   */

  import { onMount, onDestroy } from 'svelte';
  import { API_BASE_URL, API_ENDPOINTS, POLLING_INTERVALS, THERMAL_CONFIG } from '../lib/config.js';

  let { device } = $props();

  let specifications = $state(null);
  let temperatures = $state([]);
  let loading = $state(true);
  let error = $state(null);
  let pollInterval = null;
  let isPageVisible = $state(true);
  
  // Temperature statistics (raw values from measurements)
  let minTemp = $state(null);
  let maxTemp = $state(null);
  let avgTemp = $state(null);
  
  // Smoothed temperature range for stable scale display
  let smoothedMinTemp = $state(null);
  let smoothedMaxTemp = $state(null);

  /**
   * Fetch sensor specifications (grid dimensions, FOV, temperature range)
   */
  async function fetchSpecifications() {
    try {
      const response = await fetch(`${API_BASE_URL}${API_ENDPOINTS.I2C_DEVICE_SPECIFICATIONS(device.address)}`);
      if (!response.ok) {
        throw new Error(`Failed to fetch specifications: ${response.statusText}`);
      }
      const data = await response.json();
      if (data.ok && data.specifications) {
        specifications = data.specifications;
      }
    } catch (err) {
      console.error('Error fetching specifications:', err);
      error = err.message;
    }
  }

  /**
   * Fetch current thermal measurements from the sensor
   */
  async function fetchMeasurements() {
    try {
      const response = await fetch(`${API_BASE_URL}${API_ENDPOINTS.I2C_DEVICE_MEASURE(device.address)}`);
      if (!response.ok) {
        if (response.status === 408) {
          error = 'Measurement timeout';
        } else {
          throw new Error(`Failed to fetch measurements: ${response.statusText}`);
        }
        temperatures = [];
        return;
      }
      const data = await response.json();
      if (data.ok && data.measurement && data.measurement.temperatures) {
        temperatures = data.measurement.temperatures;
        calculateStatistics();
        error = null;
      }
    } catch (err) {
      console.error('Error fetching measurements:', err);
      error = err.message;
      temperatures = [];
    } finally {
      loading = false;
    }
  }

  /**
   * Calculate temperature statistics from the current measurements
   * Applies exponential smoothing to min/max for stable scale display
   */
  function calculateStatistics() {
    if (!temperatures || temperatures.length === 0) return;
    
    const allTemps = temperatures.flat();
    const currentMin = Math.min(...allTemps);
    const currentMax = Math.max(...allTemps);
    const currentAvg = allTemps.reduce((a, b) => a + b, 0) / allTemps.length;
    
    // Update raw statistics
    minTemp = currentMin;
    maxTemp = currentMax;
    avgTemp = currentAvg;
    
    // Initialize smoothed values on first measurement
    if (smoothedMinTemp === null || smoothedMaxTemp === null) {
      smoothedMinTemp = currentMin;
      smoothedMaxTemp = currentMax;
      return;
    }
    
    // Check for radical temperature changes
    const minDelta = Math.abs(currentMin - smoothedMinTemp);
    const maxDelta = Math.abs(currentMax - smoothedMaxTemp);
    const radicalChange = minDelta > THERMAL_CONFIG.TEMP_RADICAL_CHANGE_THRESHOLD || 
                          maxDelta > THERMAL_CONFIG.TEMP_RADICAL_CHANGE_THRESHOLD;
    
    // If radical change detected, update immediately; otherwise apply smoothing
    if (radicalChange) {
      smoothedMinTemp = currentMin;
      smoothedMaxTemp = currentMax;
    } else {
      // Exponential smoothing: new = old + alpha * (current - old)
      const alpha = THERMAL_CONFIG.TEMP_SCALE_SMOOTHING;
      smoothedMinTemp = smoothedMinTemp + alpha * (currentMin - smoothedMinTemp);
      smoothedMaxTemp = smoothedMaxTemp + alpha * (currentMax - smoothedMaxTemp);
    }
  }

  /**
   * Get color for temperature value using a heat map gradient
   * @param {number} temp - Temperature in Celsius
   * @returns {string} CSS color string
   */
  function getThermalColor(temp) {
    if (smoothedMinTemp === null || smoothedMaxTemp === null) return 'rgb(128, 128, 128)';
    
    // Add margin to stabilize the scale
    const minRange = smoothedMinTemp - THERMAL_CONFIG.TEMP_SCALE_MARGIN;
    const maxRange = smoothedMaxTemp + THERMAL_CONFIG.TEMP_SCALE_MARGIN;
    
    // Normalize temperature to 0-1 range
    const normalized = Math.max(0, Math.min(1, (temp - minRange) / (maxRange - minRange)));
    
    // Heat map gradient: blue -> cyan -> green -> yellow -> red
    let r, g, b;
    
    if (normalized < 0.25) {
      // Blue to Cyan
      const t = normalized / 0.25;
      r = 0;
      g = Math.round(t * 255);
      b = 255;
    } else if (normalized < 0.5) {
      // Cyan to Green
      const t = (normalized - 0.25) / 0.25;
      r = 0;
      g = 255;
      b = Math.round((1 - t) * 255);
    } else if (normalized < 0.75) {
      // Green to Yellow
      const t = (normalized - 0.5) / 0.25;
      r = Math.round(t * 255);
      g = 255;
      b = 0;
    } else {
      // Yellow to Red
      const t = (normalized - 0.75) / 0.25;
      r = 255;
      g = Math.round((1 - t) * 255);
      b = 0;
    }
    
    return `rgb(${r}, ${g}, ${b})`;
  }



  /**
   * Format temperature for display
   * @param {number} temp - Temperature in Celsius
   * @returns {string} Formatted temperature string
   */
  function formatTemperature(temp) {
    return temp.toFixed(1);
  }

  /**
   * Start polling measurements
   */
  function startPolling() {
    if (!pollInterval && isPageVisible) {
      pollInterval = setInterval(fetchMeasurements, POLLING_INTERVALS.I2C_THERMAL_SENSORS);
    }
  }

  /**
   * Stop polling measurements
   */
  function stopPolling() {
    if (pollInterval) {
      clearInterval(pollInterval);
      pollInterval = null;
    }
  }

  /**
   * Handle visibility change (tab switching)
   */
  function handleVisibilityChange() {
    isPageVisible = !document.hidden;
    
    if (isPageVisible) {
      // Resume polling when tab becomes visible
      fetchMeasurements();
      startPolling();
    } else {
      // Stop polling when tab is hidden
      stopPolling();
    }
  }

  onMount(async () => {
    // Fetch specifications once on mount
    await fetchSpecifications();
    
    // Start polling for measurements
    await fetchMeasurements();
    startPolling();
    
    // Listen for visibility changes
    document.addEventListener('visibilitychange', handleVisibilityChange);
  });

  onDestroy(() => {
    stopPolling();
    document.removeEventListener('visibilitychange', handleVisibilityChange);
  });
</script>

<div class="bg-white rounded-lg shadow p-4 border-l-4 border-red-500">
  <!-- Header -->
  <div class="flex items-start justify-between mb-3">
    <div class="flex-1">
      <h3 class="text-lg font-semibold text-gray-800 mb-1">{device.name}</h3>
      <div class="space-y-1 text-sm text-gray-600">
        <div>
          <span class="font-medium">Address:</span>
          <span class="ml-2 font-mono">0x{device.address.toString(16).toUpperCase()} ({device.address})</span>
        </div>
        {#if specifications}
          <div>
            <span class="font-medium">Grid:</span>
            <span class="ml-2">{specifications.width} × {specifications.height}</span>
            <span class="ml-3 font-medium">FOV:</span>
            <span class="ml-2">{specifications.horizontalFOVDeg}° × {specifications.verticalFOVDeg}°</span>
            <span class="ml-3 font-medium">Rate:</span>
            <span class="ml-2">{specifications.updateRateHz} Hz</span>
          </div>
          <div>
            <span class="font-medium">Range:</span>
            <span class="ml-2">{specifications.minTempCelsius}°C to {specifications.maxTempCelsius}°C</span>
            <span class="ml-3 font-medium">Resolution:</span>
            <span class="ml-2">{specifications.resolutionCelsius}°C</span>
          </div>
        {/if}
      </div>
    </div>
    <div class="ml-4">
      <span class="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-red-100 text-red-700">
        {device.type}
      </span>
    </div>
  </div>

  <!-- Error State -->
  {#if error}
    <div class="mt-3 p-3 bg-red-50 border border-red-200 text-red-700 rounded text-sm">
      <span class="font-medium">Error:</span> {error}
    </div>
  {/if}

  <!-- Loading State -->
  {#if loading}
    <div class="mt-3 flex items-center justify-center p-4">
      <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-red-600"></div>
      <span class="ml-3 text-gray-600">Loading measurements...</span>
    </div>
  {:else if temperatures.length > 0}
    <!-- Temperature Statistics -->
    {#if minTemp !== null && maxTemp !== null && avgTemp !== null}
      <div class="mt-3 p-3 bg-gray-50 rounded-lg border border-gray-200">
        <div class="grid grid-cols-3 gap-4 text-center text-sm">
          <div>
            <div class="text-xs text-gray-600 font-medium mb-1">MIN</div>
            <div class="text-lg font-bold text-blue-600">{formatTemperature(minTemp)}°C</div>
          </div>
          <div>
            <div class="text-xs text-gray-600 font-medium mb-1">AVG</div>
            <div class="text-lg font-bold text-gray-700">{formatTemperature(avgTemp)}°C</div>
          </div>
          <div>
            <div class="text-xs text-gray-600 font-medium mb-1">MAX</div>
            <div class="text-lg font-bold text-red-600">{formatTemperature(maxTemp)}°C</div>
          </div>
        </div>
      </div>
    {/if}

    <!-- Heatmap View -->
    <div class="mt-3">
      <div class="text-xs font-medium text-gray-600 mb-2">
        Thermal Heatmap (interpolated view)
      </div>
        <div class="relative rounded-lg overflow-hidden border border-gray-300">
          <!-- Create a larger canvas for interpolation effect -->
          <div 
            class="grid" 
            style="grid-template-columns: repeat({(specifications?.width || 8) * 4}, 1fr);"
          >
            {#each Array((specifications?.height || 8) * 4) as _, rowIdx}
              {#each Array((specifications?.width || 8) * 4) as _, colIdx}
                {@const origRow = Math.floor(rowIdx / 4)}
                {@const origCol = Math.floor(colIdx / 4)}
                {@const temp = temperatures[origRow]?.[origCol] || 0}
                <div 
                  class="aspect-square"
                  style="background-color: {getThermalColor(temp)};"
                ></div>
              {/each}
            {/each}
          </div>
          
          <!-- Overlay temperature values at original grid positions -->
          <div 
            class="absolute inset-0 grid gap-1 p-1"
            style="grid-template-columns: repeat({specifications?.width || 8}, 1fr);"
          >
            {#each temperatures as row}
              {#each row as temp}
                <div class="flex items-center justify-center">
                  <div 
                    class="text-xs font-bold px-1 py-0.5 rounded bg-black bg-opacity-30"
                    style="color: white; text-shadow: 0 0 2px rgba(0,0,0,0.8);"
                  >
                    {formatTemperature(temp)}°
                  </div>
                </div>
              {/each}
            {/each}
          </div>
        </div>
        
        <!-- Color scale legend -->
        {#if smoothedMinTemp !== null && smoothedMaxTemp !== null}
          <div class="mt-3">
            <div class="text-xs font-medium text-gray-600 mb-2">Temperature Scale</div>
            <div class="flex items-center gap-2">
              <span class="text-xs text-gray-600 font-mono">{formatTemperature(smoothedMinTemp - THERMAL_CONFIG.TEMP_SCALE_MARGIN)}°C</span>
              <div class="flex-1 h-6 rounded" style="background: linear-gradient(to right, 
                rgb(0, 0, 255), 
                rgb(0, 255, 255), 
                rgb(0, 255, 0), 
                rgb(255, 255, 0), 
                rgb(255, 0, 0)
              );"></div>
              <span class="text-xs text-gray-600 font-mono">{formatTemperature(smoothedMaxTemp + THERMAL_CONFIG.TEMP_SCALE_MARGIN)}°C</span>
            </div>
          </div>
        {/if}
    </div>
  {:else if !error}
    <!-- No Data State -->
    <div class="mt-3 p-3 bg-gray-50 rounded text-sm text-gray-600 text-center">
      No measurements available
    </div>
  {/if}
</div>
