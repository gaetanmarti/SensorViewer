<script>
  /**
   * EnvironmentalSensor Component
   *
   * Displays real-time environmental measurements from I2C sensors (e.g., BME680).
   * Automatically polls the sensor and displays:
   * - Device information and supported capabilities
   * - Current values (temperature, humidity, pressure, gas resistance)
   * - Historical graphs for each available measurement
   *
   * @component
   * @prop {Object} device - The I2C environmental sensor device object
   * @prop {number} device.address - I2C address in decimal format
   * @prop {string} device.name - Device name
   * @prop {string} device.type - Device type (should be "Environmental")
   */

  import { onMount, onDestroy, tick } from 'svelte';
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
  let temperatureHistory = $state([]);
  let humidityHistory = $state([]);
  let pressureHistory = $state([]);
  let gasHistory = $state([]);
  let iaqHistory = $state([]);

  // Chart.js instances
  let temperatureCanvas = $state(null);
  let humidityCanvas = $state(null);
  let pressureCanvas = $state(null);
  let gasCanvas = $state(null);
  let iaqCanvas = $state(null);
  let temperatureChart;
  let humidityChart;
  let pressureChart;
  let gasChart;
  let iaqChart;

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

    if (measurement.temperatureCelsius !== null && measurement.temperatureCelsius !== undefined) {
      temperatureHistory = [...temperatureHistory, measurement.temperatureCelsius].slice(-MAX_HISTORY);
    }

    if (measurement.humidityPercent !== null && measurement.humidityPercent !== undefined) {
      humidityHistory = [...humidityHistory, measurement.humidityPercent].slice(-MAX_HISTORY);
    }

    if (measurement.pressureHPa !== null && measurement.pressureHPa !== undefined) {
      pressureHistory = [...pressureHistory, measurement.pressureHPa].slice(-MAX_HISTORY);
    }

    if (measurement.gasResistanceOhms !== null && measurement.gasResistanceOhms !== undefined) {
      gasHistory = [...gasHistory, measurement.gasResistanceOhms].slice(-MAX_HISTORY);
    }

    if (measurement.iaqIndex !== null && measurement.iaqIndex !== undefined) {
      iaqHistory = [...iaqHistory, measurement.iaqIndex].slice(-MAX_HISTORY);
    }
  }

  /**
   * Create a Chart.js chart
   */
  function createChart(canvas, color, label, decimals = 2) {
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
              label: (context) => `${label}: ${context.parsed.y.toFixed(decimals)}`
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

  function hasValue(value) {
    return value !== null && value !== undefined;
  }

  function formatTemperature(temp) {
    return temp.toFixed(2);
  }

  function formatHumidity(humidity) {
    return humidity.toFixed(2);
  }

  function formatPressure(pressure) {
    return pressure.toFixed(2);
  }

  function formatGas(gas) {
    return gas.toFixed(0);
  }

  function formatIAQ(iaq) {
    return iaq.toFixed(1);
  }

  function getIAQCategory(iaq) {
    if (iaq <= 50) return 'Good';
    if (iaq <= 150) return 'Moderate';
    if (iaq <= 175) return 'Unhealthy for Sensitive Groups';
    if (iaq <= 200) return 'Unhealthy';
    if (iaq <= 300) return 'Very Unhealthy';
    return 'Hazardous';
  }

  function getIAQCategoryClass(iaq) {
    if (iaq <= 50) return 'bg-green-100 text-green-800 border-green-300';
    if (iaq <= 150) return 'bg-yellow-100 text-yellow-800 border-yellow-300';
    if (iaq <= 175) return 'bg-orange-100 text-orange-800 border-orange-300';
    if (iaq <= 200) return 'bg-red-100 text-red-800 border-red-300';
    if (iaq <= 300) return 'bg-purple-100 text-purple-800 border-purple-300';
    return 'bg-rose-100 text-rose-800 border-rose-300';
  }

  /**
   * Start polling measurements
   */
  function startPolling() {
    if (!pollInterval && isPageVisible) {
      pollInterval = setInterval(fetchMeasurements, POLLING_INTERVALS.I2C_ENVIRONMENTAL_SENSORS);
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

    // Wait one microtask so bind:this canvases are mounted.
    await tick();

    // Create charts
    temperatureChart = createChart(temperatureCanvas, 'rgba(239, 68, 68, 1)', 'Temperature (degC)');
    humidityChart = createChart(humidityCanvas, 'rgba(59, 130, 246, 1)', 'Humidity (%)');
    pressureChart = createChart(pressureCanvas, 'rgba(99, 102, 241, 1)', 'Pressure (hPa)');
    gasChart = createChart(gasCanvas, 'rgba(16, 185, 129, 1)', 'Gas (Ohms)', 0);
    iaqChart = createChart(iaqCanvas, 'rgba(245, 158, 11, 1)', 'IAQ Index', 1);
  });

  onDestroy(() => {
    stopPolling();
    document.removeEventListener('visibilitychange', handleVisibilityChange);

    // Destroy charts
    if (temperatureChart) temperatureChart.destroy();
    if (humidityChart) humidityChart.destroy();
    if (pressureChart) pressureChart.destroy();
    if (gasChart) gasChart.destroy();
    if (iaqChart) iaqChart.destroy();
  });

  // Update charts when history changes
  $effect(() => {
    if (temperatureHistory.length > 0) {
      updateChart(temperatureChart, temperatureHistory);
    }
  });

  $effect(() => {
    if (humidityHistory.length > 0) {
      updateChart(humidityChart, humidityHistory);
    }
  });

  $effect(() => {
    if (pressureHistory.length > 0) {
      updateChart(pressureChart, pressureHistory);
    }
  });

  $effect(() => {
    if (gasHistory.length > 0) {
      updateChart(gasChart, gasHistory);
    }
  });

  $effect(() => {
    if (iaqHistory.length > 0) {
      updateChart(iaqChart, iaqHistory);
    }
  });
</script>

<div class="bg-white rounded-lg shadow p-4 border-l-4 border-emerald-500">
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
          <div class="flex flex-wrap gap-x-4 gap-y-1">
            {#if specifications.hasTemperature}
              <span><span class="font-medium">Temperature:</span> Yes</span>
            {/if}
            {#if specifications.hasHumidity}
              <span><span class="font-medium">Humidity:</span> Yes</span>
            {/if}
            {#if specifications.hasPressure}
              <span><span class="font-medium">Pressure:</span> Yes</span>
            {/if}
            {#if specifications.hasGas}
              <span><span class="font-medium">Gas:</span> Yes</span>
            {/if}
          </div>
        {/if}
      </div>
    </div>
    <div class="ml-4">
      <span class="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-emerald-100 text-emerald-700">
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
      <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-emerald-600"></div>
      <span class="ml-3 text-gray-600">Loading measurements...</span>
    </div>
  {:else if measurement}
    <!-- Current Values -->
    <div class="mt-3 p-3 bg-gray-50 rounded-lg border border-gray-200">
      <div class="grid grid-cols-2 md:grid-cols-5 gap-3 text-center text-sm">
        <div class="p-2 bg-white rounded border border-gray-200">
          <div class="text-xs text-gray-600 font-medium mb-1">TEMPERATURE</div>
          <div class="text-lg font-bold text-red-600">
            {#if hasValue(measurement.temperatureCelsius)}
              {formatTemperature(measurement.temperatureCelsius)}°C
            {:else}
              <span class="text-sm font-medium text-gray-400">N/A</span>
            {/if}
          </div>
        </div>

        <div class="p-2 bg-white rounded border border-gray-200">
          <div class="text-xs text-gray-600 font-medium mb-1">HUMIDITY</div>
          <div class="text-lg font-bold text-blue-600">
            {#if hasValue(measurement.humidityPercent)}
              {formatHumidity(measurement.humidityPercent)}%
            {:else}
              <span class="text-sm font-medium text-gray-400">N/A</span>
            {/if}
          </div>
        </div>

        <div class="p-2 bg-white rounded border border-gray-200">
          <div class="text-xs text-gray-600 font-medium mb-1">PRESSURE</div>
          <div class="text-lg font-bold text-indigo-600">
            {#if hasValue(measurement.pressureHPa)}
              {formatPressure(measurement.pressureHPa)} hPa
            {:else}
              <span class="text-sm font-medium text-gray-400">N/A</span>
            {/if}
          </div>
        </div>

        <div class="p-2 bg-white rounded border border-gray-200">
          <div class="text-xs text-gray-600 font-medium mb-1">GAS RESISTANCE</div>
          <div class="text-lg font-bold text-emerald-600">
            {#if hasValue(measurement.gasResistanceOhms)}
              {formatGas(measurement.gasResistanceOhms)} ohm
            {:else}
              <span class="text-sm font-medium text-gray-400">N/A</span>
            {/if}
          </div>
        </div>

        <div class="p-2 bg-white rounded border border-gray-200">
          <div class="text-xs text-gray-600 font-medium mb-1">IAQ INDEX</div>
          <div class="text-lg font-bold text-amber-600">
            {#if hasValue(measurement.iaqIndex)}
              {formatIAQ(measurement.iaqIndex)}
            {:else}
              <span class="text-sm font-medium text-gray-400">N/A</span>
            {/if}
          </div>
          {#if hasValue(measurement.iaqIndex)}
            <div class="mt-1 inline-flex px-2 py-0.5 rounded text-[10px] font-medium border {getIAQCategoryClass(measurement.iaqIndex)}">
              {getIAQCategory(measurement.iaqIndex)}
            </div>
          {/if}
        </div>
      </div>
    </div>

    <!-- Graphs -->
    <div class="mt-4 space-y-4">
      {#if hasValue(measurement.temperatureCelsius)}
        <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
          <div class="flex items-center justify-between mb-2 px-1">
            <div class="text-sm font-medium text-gray-700">Temperature</div>
            <div class="text-xs text-gray-500">{formatTemperature(measurement.temperatureCelsius)}°C</div>
          </div>
          <div class="h-16">
            <canvas bind:this={temperatureCanvas}></canvas>
          </div>
        </div>
      {/if}

      {#if hasValue(measurement.humidityPercent)}
        <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
          <div class="flex items-center justify-between mb-2 px-1">
            <div class="text-sm font-medium text-gray-700">Humidity</div>
            <div class="text-xs text-gray-500">{formatHumidity(measurement.humidityPercent)}%</div>
          </div>
          <div class="h-16">
            <canvas bind:this={humidityCanvas}></canvas>
          </div>
        </div>
      {/if}

      {#if hasValue(measurement.pressureHPa)}
        <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
          <div class="flex items-center justify-between mb-2 px-1">
            <div class="text-sm font-medium text-gray-700">Pressure</div>
            <div class="text-xs text-gray-500">{formatPressure(measurement.pressureHPa)} hPa</div>
          </div>
          <div class="h-16">
            <canvas bind:this={pressureCanvas}></canvas>
          </div>
        </div>
      {/if}

      {#if hasValue(measurement.gasResistanceOhms)}
        <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
          <div class="flex items-center justify-between mb-2 px-1">
            <div class="text-sm font-medium text-gray-700">Gas Resistance</div>
            <div class="text-xs text-gray-500">{formatGas(measurement.gasResistanceOhms)} ohm</div>
          </div>
          <div class="h-16">
            <canvas bind:this={gasCanvas}></canvas>
          </div>
        </div>
      {/if}

      {#if hasValue(measurement.iaqIndex)}
        <div class="px-2 py-3 bg-gray-50 rounded-lg border border-gray-200">
          <div class="flex items-center justify-between mb-2 px-1">
            <div class="text-sm font-medium text-gray-700">IAQ Index</div>
            <div class="text-xs text-gray-500">{formatIAQ(measurement.iaqIndex)} ({getIAQCategory(measurement.iaqIndex)})</div>
          </div>
          <div class="h-16">
            <canvas bind:this={iaqCanvas}></canvas>
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
