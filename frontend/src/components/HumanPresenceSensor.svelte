<script>
  /**
   * HumanPresenceSensor Component
   * 
   * Displays real-time human presence and motion detection from I2C sensors (e.g., STHS34PF80).
   * Automatically polls the sensor at a configurable interval and displays:
   * - Device information (name, address, specifications)
   * - Current temperature measurements (ambient and object)
   * - Detection status (presence, motion, ambient shock)
   * - Historical graphs for presence, motion, object temperature, and ambient shock
   * 
   * @component
   * @prop {Object} device - The I2C human presence sensor device object
   * @prop {number} device.address - I2C address in decimal format
   * @prop {string} device.name - Device name
   * @prop {string} device.type - Device type (should be "HumanPresence")
   */

  import { onMount, onDestroy } from 'svelte';
  import { Chart } from 'chart.js/auto';
  import { API_BASE_URL, API_ENDPOINTS, POLLING_INTERVALS } from '../lib/config.js';

  let { device } = $props();

  let specifications = $state(null);
  let measurement = $state(null);
  let loading = $state(true);
  let error = $state(null);
  let pollInterval = null;
  let isPageVisible = $state(true);
  
  // History for graphs (keep last 100 measurements)
  const MAX_HISTORY = 100;
  let presenceHistory = $state([]);
  let motionHistory = $state([]);
  let objectTempHistory = $state([]);
  let ambientShockHistory = $state([]);

  // Chart.js instances
  let presenceCanvas;
  let motionCanvas;
  let objectTempCanvas;
  let ambientShockCanvas;
  let presenceChart;
  let motionChart;
  let objectTempChart;
  let ambientShockChart;

  /**
   * Fetch sensor specifications
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
   * Fetch current measurements from the sensor
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
        measurement = null;
        return;
      }
      const data = await response.json();
      if (data.ok && data.measurement) {
        measurement = data.measurement;
        updateHistory();
        error = null;
      }
    } catch (err) {
      console.error('Error fetching measurements:', err);
      error = err.message;
      measurement = null;
    } finally {
      loading = false;
    }
  }

  /**
   * Update history arrays with current measurement
   */
  function updateHistory() {
    if (!measurement) return;
    
    // Add new values
    presenceHistory = [...presenceHistory, measurement.presenceValue].slice(-MAX_HISTORY);
    motionHistory = [...motionHistory, measurement.motionValue].slice(-MAX_HISTORY);
    objectTempHistory = [...objectTempHistory, measurement.objectTemperatureCelsius].slice(-MAX_HISTORY);
    ambientShockHistory = [...ambientShockHistory, measurement.ambientShockValue].slice(-MAX_HISTORY);
  }

  /**
   * Create a Chart.js chart
   */
  function createChart(canvas, color, label) {
    if (!canvas) return null;
    
    const ctx = canvas.getContext('2d');
    
    return new Chart(ctx, {
      type: 'line',
      data: {
        labels: [],
        datasets: [{
          data: [],
          borderColor: color,
          backgroundColor: color.replace('1)', '0.2)'),
          borderWidth: 2,
          tension: 0.4,
          pointRadius: 2,
          pointHoverRadius: 4,
          fill: true,
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: { 
            enabled: true,
            callbacks: {
              label: (context) => `${label}: ${context.parsed.y.toFixed(2)}`
            }
          }
        },
        scales: {
          x: { display: false },
          y: {
            display: true,
            position: 'right',
            ticks: {
              font: { size: 10 },
              color: '#9CA3AF'
            },
            grid: {
              color: 'rgba(156, 163, 175, 0.1)'
            }
          }
        },
        animation: false,
      }
    });
  }

  /**
   * Update a chart with new data
   */
  function updateChart(chart, data) {
    if (!chart || !data || data.length === 0) return;
    
    chart.data.labels = Array(data.length).fill('');
    chart.data.datasets[0].data = [...data];
    chart.update('none');
  }

  /**
   * Format temperature for display
   * @param {number} temp - Temperature in Celsius
   * @returns {string} Formatted temperature string
   */
  function formatTemperature(temp) {
    return temp.toFixed(2);
  }

  /**
   * Format value for display
   * @param {number} value - Value to format
   * @returns {string} Formatted value string
   */
  function formatValue(value) {
    return value.toFixed(0);
  }

  /**
   * Start polling measurements
   */
  function startPolling() {
    if (!pollInterval && isPageVisible) {
      pollInterval = setInterval(fetchMeasurements, POLLING_INTERVALS.I2C_HUMAN_PRESENCE_SENSORS);
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
      fetchMeasurements();
      startPolling();
    } else {
      stopPolling();
    }
  }

  onMount(async () => {
    await fetchSpecifications();
    await fetchMeasurements();
    startPolling();
    
    document.addEventListener('visibilitychange', handleVisibilityChange);
    
    // Create charts
    presenceChart = createChart(presenceCanvas, 'rgba(16, 185, 129, 1)', 'Presence');
    motionChart = createChart(motionCanvas, 'rgba(234, 179, 8, 1)', 'Motion');
    objectTempChart = createChart(objectTempCanvas, 'rgba(239, 68, 68, 1)', 'Object Temp');
    ambientShockChart = createChart(ambientShockCanvas, 'rgba(249, 115, 22, 1)', 'Ambient Shock');
  });

  onDestroy(() => {
    stopPolling();
    document.removeEventListener('visibilitychange', handleVisibilityChange);
    
    // Destroy charts
    if (presenceChart) presenceChart.destroy();
    if (motionChart) motionChart.destroy();
    if (objectTempChart) objectTempChart.destroy();
    if (ambientShockChart) ambientShockChart.destroy();
  });

  // Update charts when history changes
  $effect(() => {
    if (presenceHistory.length > 0) {
      updateChart(presenceChart, presenceHistory);
    }
  });

  $effect(() => {
    if (motionHistory.length > 0) {
      updateChart(motionChart, motionHistory);
    }
  });

  $effect(() => {
    if (objectTempHistory.length > 0) {
      updateChart(objectTempChart, objectTempHistory);
    }
  });

  $effect(() => {
    if (ambientShockHistory.length > 0) {
      updateChart(ambientShockChart, ambientShockHistory);
    }
  });
</script>

<div class="bg-white rounded-lg shadow p-4 border-l-4 border-purple-500">
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
            <span class="font-medium">FOV:</span>
            <span class="ml-2">{specifications.horizontalFOVDeg}° × {specifications.verticalFOVDeg}°</span>
            <span class="ml-3 font-medium">Rate:</span>
            <span class="ml-2">{specifications.updateRateHz} Hz</span>
            <span class="ml-3 font-medium">Range:</span>
            <span class="ml-2">{specifications.detectionRangeMeters} m</span>
          </div>
          <div>
            <span class="font-medium">Temp Range:</span>
            <span class="ml-2">{specifications.minTempCelsius}°C to {specifications.maxTempCelsius}°C</span>
            <span class="ml-3 font-medium">Resolution:</span>
            <span class="ml-2">{specifications.resolutionCelsius}°C</span>
          </div>
        {/if}
      </div>
    </div>
    <div class="ml-4">
      <span class="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-purple-100 text-purple-700">
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
      <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-purple-600"></div>
      <span class="ml-3 text-gray-600">Loading measurements...</span>
    </div>
  {:else if measurement}
    <!-- Temperature Display -->
    <div class="mt-3 p-3 bg-gray-50 rounded-lg border border-gray-200">
      <div class="grid grid-cols-2 gap-4 text-center text-sm">
        <div>
          <div class="text-xs text-gray-600 font-medium mb-1">AMBIENT TEMP</div>
          <div class="text-lg font-bold text-blue-600">{formatTemperature(measurement.ambientTemperatureCelsius)}°C</div>
        </div>
        <div>
          <div class="text-xs text-gray-600 font-medium mb-1">OBJECT TEMP</div>
          <div class="text-lg font-bold text-red-600">{formatTemperature(measurement.objectTemperatureCelsius)}°C</div>
        </div>
      </div>
    </div>

    <!-- Detection Status -->
    <div class="mt-3 p-3 bg-gray-50 rounded-lg border border-gray-200">
      <div class="grid grid-cols-3 gap-2 text-center text-xs">
        <div class="p-2 rounded {measurement.presenceDetected ? 'bg-green-100 text-green-800 border border-green-300' : 'bg-gray-100 text-gray-500 border border-gray-300'}">
          <div class="font-medium">PRESENCE</div>
          <div class="mt-1">{measurement.presenceDetected ? 'Detected' : 'None'}</div>
        </div>
        <div class="p-2 rounded {measurement.motionDetected ? 'bg-yellow-100 text-yellow-800 border border-yellow-300' : 'bg-gray-100 text-gray-500 border border-gray-300'}">
          <div class="font-medium">MOTION</div>
          <div class="mt-1">{measurement.motionDetected ? 'Detected' : 'None'}</div>
        </div>
        <div class="p-2 rounded {measurement.ambientShockDetected ? 'bg-orange-100 text-orange-800 border border-orange-300' : 'bg-gray-100 text-gray-500 border border-gray-300'}">
          <div class="font-medium">AMB. SHOCK</div>
          <div class="mt-1">{measurement.ambientShockDetected ? 'Detected' : 'None'}</div>
        </div>
      </div>
    </div>

    <!-- Graphs -->
    <div class="mt-4 space-y-4">
      <!-- Presence Graph -->
      <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
        <div class="flex items-center justify-between mb-2 px-1">
          <div class="text-sm font-medium text-gray-700">Presence Value</div>
          <div class="text-xs text-gray-500">{formatValue(measurement.presenceValue)}</div>
        </div>
        <div class="h-16">
          <canvas bind:this={presenceCanvas}></canvas>
        </div>
      </div>

      <!-- Motion Graph -->
      <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
        <div class="flex items-center justify-between mb-2 px-1">
          <div class="text-sm font-medium text-gray-700">Motion Value</div>
          <div class="text-xs text-gray-500">{formatValue(measurement.motionValue)}</div>
        </div>
        <div class="h-16">
          <canvas bind:this={motionCanvas}></canvas>
        </div>
      </div>

      <!-- Object Temperature Graph -->
      <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
        <div class="flex items-center justify-between mb-2 px-1">
          <div class="text-sm font-medium text-gray-700">Object Temperature</div>
          <div class="text-xs text-gray-500">{formatTemperature(measurement.objectTemperatureCelsius)}°C</div>
        </div>
        <div class="h-16">
          <canvas bind:this={objectTempCanvas}></canvas>
        </div>
      </div>

      <!-- Ambient Shock Graph -->
      <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
        <div class="flex items-center justify-between mb-2 px-1">
          <div class="text-sm font-medium text-gray-700">Ambient Shock Value</div>
          <div class="text-xs text-gray-500">{formatValue(measurement.ambientShockValue)}</div>
        </div>
        <div class="h-16">
          <canvas bind:this={ambientShockCanvas}></canvas>
        </div>
      </div>
    </div>
  {:else if !error}
    <!-- No Data State -->
    <div class="mt-3 p-3 bg-gray-50 rounded text-sm text-gray-600 text-center">
      No measurements available
    </div>
  {/if}
</div>
