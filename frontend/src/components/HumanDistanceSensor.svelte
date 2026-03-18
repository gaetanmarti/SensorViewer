<script>
  /**
   * HumanDistanceSensor Component
   *
   * Displays real-time human detection data from a virtual Human Distance sensor
   * (DistanceHumanPresenceSensor), derived from an underlying I2C distance (ToF) sensor.
   * Automatically polls the sensor at a configurable interval and displays:
   * - Device information (name, address, specifications)
   * - Detection status (presence boolean)
   * - 3-D position of the detected human (x, y, z in metres)
   * - Detection quality indicator
   * - Historical graph of distance over time
   *
   * @component
   * @prop {Object} device - The I2C virtual human distance sensor device object
   * @prop {number} device.address - I2C address in decimal format
   * @prop {string} device.name - Device name
   * @prop {string} device.type - Device type (should be "HumanDistance")
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

  // History for distance graph (keep last 100 measurements)
  const MAX_HISTORY = 100;
  let distanceHistory = $state([]);
  let qualityHistory = $state([]);

  // Chart.js instances and canvas elements
  let distanceCanvas = $state(null);
  let qualityCanvas = $state(null);
  let distanceChart = $state(null);
  let qualityChart = $state(null);

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
   * Fetch current measurement from the sensor
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
    const dist = measurement.presence && measurement.position ? measurement.position.distance : 0;
    distanceHistory = [...distanceHistory, dist].slice(-MAX_HISTORY);
    qualityHistory = [...qualityHistory, measurement.quality01].slice(-MAX_HISTORY);
  }

  /**
   * Create a Chart.js line chart
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
              label: (context) => `${label}: ${context.parsed.y.toFixed(3)}`
            }
          }
        },
        scales: {
          x: { display: false },
          y: {
            display: true,
            position: 'right',
            ticks: { font: { size: 10 }, color: '#9CA3AF' },
            grid: { color: 'rgba(156, 163, 175, 0.1)' }
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
   * Format a coordinate value in metres
   */
  function formatCoord(val) {
    return val.toFixed(3);
  }

  /**
   * Get quality colour class based on quality01 value
   */
  function getQualityColorClass(q) {
    if (q >= 0.7) return 'text-green-600';
    if (q >= 0.4) return 'text-yellow-500';
    return 'text-red-500';
  }

  function startPolling() {
    if (!pollInterval && isPageVisible) {
      pollInterval = setInterval(fetchMeasurements, POLLING_INTERVALS.I2C_HUMAN_DISTANCE_SENSORS);
    }
  }

  function stopPolling() {
    if (pollInterval) {
      clearInterval(pollInterval);
      pollInterval = null;
    }
  }

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
  });

  onDestroy(() => {
    stopPolling();
    document.removeEventListener('visibilitychange', handleVisibilityChange);
    if (distanceChart) distanceChart.destroy();
    if (qualityChart) qualityChart.destroy();
  });

  // Destroy charts when an error occurs
  $effect(() => {
    if (error) {
      if (distanceChart) { distanceChart.destroy(); distanceChart = null; }
      if (qualityChart) { qualityChart.destroy(); qualityChart = null; }
    }
  });

  // Create and update distance chart
  $effect(() => {
    if (distanceCanvas && !distanceChart) {
      distanceChart = createChart(distanceCanvas, 'rgba(59, 130, 246, 1)', 'Distance (m)');
    }
    if (distanceChart && distanceHistory.length > 0) {
      updateChart(distanceChart, distanceHistory);
    }
  });

  // Create and update quality chart
  $effect(() => {
    if (qualityCanvas && !qualityChart) {
      qualityChart = createChart(qualityCanvas, 'rgba(16, 185, 129, 1)', 'Quality');
    }
    if (qualityChart && qualityHistory.length > 0) {
      updateChart(qualityChart, qualityHistory);
    }
  });
</script>

<div class="w-full h-full bg-white rounded-lg shadow p-4 border-l-4 border-blue-500 flex flex-col">
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
            <span class="ml-3 font-medium">Max Range:</span>
            <span class="ml-2">{specifications.maxRangeMeters} m</span>
          </div>
        {/if}
      </div>
    </div>
    <div class="ml-4">
      <span class="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-blue-100 text-blue-700">
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
      <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      <span class="ml-3 text-gray-600">Loading measurements...</span>
    </div>
  {:else if measurement || distanceHistory.length > 0}
    <!-- Detection Status -->
    {#if measurement}
      <div class="mt-3 p-3 bg-gray-50 rounded-lg border border-gray-200">
        <div class="grid grid-cols-2 gap-4 text-center">
          <!-- Presence -->
          <div class="p-2 rounded text-xs font-medium {measurement.presence ? 'bg-green-100 text-green-800 border border-green-300' : 'bg-gray-100 text-gray-500 border border-gray-300'}">
            <div>PRESENCE</div>
            <div class="mt-1 text-sm font-bold">{measurement.presence ? 'Detected' : 'None'}</div>
          </div>
          <!-- Quality -->
          <div class="p-2 rounded bg-gray-50 border border-gray-200 text-xs">
            <div class="font-medium text-gray-600">QUALITY</div>
            <div class="mt-1 text-sm font-bold {getQualityColorClass(measurement.quality01)}">
              {(measurement.quality01 * 100).toFixed(1)}%
            </div>
          </div>
        </div>
      </div>

      <!-- 3D Position -->
      {#if measurement.presence && measurement.position}
        <div class="mt-3 p-3 bg-gray-50 rounded-lg border border-gray-200">
          <div class="text-xs text-gray-600 font-medium mb-2 uppercase tracking-wide">Position (metres)</div>
          <div class="grid grid-cols-4 gap-2 text-center text-sm">
            <div>
              <div class="text-xs text-gray-500 font-medium">X</div>
              <div class="font-mono font-bold text-gray-800">{formatCoord(measurement.position.x)}</div>
            </div>
            <div>
              <div class="text-xs text-gray-500 font-medium">Y</div>
              <div class="font-mono font-bold text-gray-800">{formatCoord(measurement.position.y)}</div>
            </div>
            <div>
              <div class="text-xs text-gray-500 font-medium">Z</div>
              <div class="font-mono font-bold text-gray-800">{formatCoord(measurement.position.z)}</div>
            </div>
            <div>
              <div class="text-xs text-gray-500 font-medium">DIST</div>
              <div class="font-mono font-bold text-blue-600">{formatCoord(measurement.position.distance)} m</div>
            </div>
          </div>
        </div>
      {/if}
    {/if}

    <!-- Graphs -->
    {#if !error}
      <div class="mt-4 space-y-4">
        <!-- Distance Graph -->
        <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
          <div class="flex items-center justify-between mb-2 px-1">
            <div class="text-sm font-medium text-gray-700">Distance (m)</div>
            <div class="text-xs text-gray-500">
              {measurement && measurement.presence && measurement.position ? formatCoord(measurement.position.distance) + ' m' : '-'}
            </div>
          </div>
          <div class="h-16">
            <canvas bind:this={distanceCanvas}></canvas>
          </div>
        </div>

        <!-- Quality Graph -->
        <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
          <div class="flex items-center justify-between mb-2 px-1">
            <div class="text-sm font-medium text-gray-700">Detection Quality</div>
            <div class="text-xs text-gray-500">
              {measurement ? (measurement.quality01 * 100).toFixed(1) + '%' : '-'}
            </div>
          </div>
          <div class="h-16">
            <canvas bind:this={qualityCanvas}></canvas>
          </div>
        </div>
      </div>
    {/if}
  {:else if !error}
    <div class="mt-3 p-3 bg-gray-50 rounded text-sm text-gray-600 text-center">
      No measurements available
    </div>
  {/if}
</div>
