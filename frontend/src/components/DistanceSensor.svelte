<script>
  /**
   * DistanceSensor Component
   * 
   * Displays real-time distance measurements from I2C Time-of-Flight (ToF) sensors.
   * Automatically polls the sensor at a configurable interval and displays:
   * - Device information (name, address, specifications)
   * - Live distance measurements in a grid layout
   * - Confidence levels with color-coded indicators
   * 
   * @component
   * @prop {Object} device - The I2C distance sensor device object
   * @prop {number} device.address - I2C address in decimal format
   * @prop {string} device.name - Device name
   * @prop {string} device.type - Device type (should be "Distance")
   */

  import { onMount, onDestroy } from 'svelte';
  import { API_BASE_URL, API_ENDPOINTS, POLLING_INTERVALS } from '../lib/config.js';

  let { device } = $props();

  let specifications = $state(null);
  let measurements = $state([]);
  let loading = $state(true);
  let error = $state(null);
  let pollInterval = null;
  let isPageVisible = $state(true);
  let activeTab = $state('grid'); // 'grid' or '3d'
  
  // 3D view controls
  let rotationX = $state(-60);
  let rotationY = $state(180);
  let isDragging = $state(false);
  let dragStartX = $state(0);
  let dragStartY = $state(0);
  let zoom = $state(1.0);

  /**
   * Fetch sensor specifications (grid dimensions, FOV, update rate)
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
   * Fetch current distance measurements from the sensor
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
        measurements = [];
        return;
      }
      const data = await response.json();
      if (data.ok && data.measurement) {
        measurements = data.measurement;
        error = null;
      }
    } catch (err) {
      console.error('Error fetching measurements:', err);
      error = err.message;
      measurements = [];
    } finally {
      loading = false;
    }
  }

  /**
   * Get color class based on confidence level
   * @param {number} confidence - Confidence value (0.0 to 1.0)
   * @returns {string} Tailwind CSS classes for color coding
   */
  function getConfidenceColor(confidence) {
    if (confidence >= 0.8) return 'bg-green-100 text-green-800 border-green-300';
    if (confidence >= 0.5) return 'bg-yellow-100 text-yellow-800 border-yellow-300';
    return 'bg-red-100 text-red-800 border-red-300';
  }

  /**
   * Format distance value for display
   * @param {number} distMM - Distance in millimeters
   * @returns {string} Formatted distance string
   */
  function formatDistance(distMM) {
    if (distMM >= 1000) {
      return `${(distMM / 1000).toFixed(2)} m`;
    }
    return `${distMM} mm`;
  }

  /**
   * Get normalized height for 3D visualization
   * @param {number} distMM - Distance in millimeters
   * @returns {number} Height percentage (0-100)
   */
  function getBarHeight(distMM) {
    // Normalize distance to 0-100% scale (max distance = 2000mm)
    const maxDistance = 2000;
    const minHeight = 10;
    const height = 100 - ((distMM / maxDistance) * (100 - minHeight));
    return Math.max(minHeight, Math.min(100, height));
  }

  /**
   * Calculate 3D position for a zone based on FOV and distance
   * @param {number} index - Zone index
   * @param {number} distMM - Distance in millimeters
   * @returns {Object} 3D position {x, y, z, angleH, angleV}
   */
  function calculate3DPosition(index, distMM) {
    const width = specifications?.width || 3;
    const height = specifications?.height || 3;
    const hFOV = specifications?.horizontalFOVDeg || 32;
    const vFOV = specifications?.verticalFOVDeg || 33;
    
    const row = Math.floor(index / width);
    const col = index % width;
    
    // Calculate angular position for each zone
    // Center of sensor is 0,0
    const angleStepH = hFOV / width;
    const angleStepV = vFOV / height;
    
    const angleH = (col - (width - 1) / 2) * angleStepH;
    const angleV = ((height - 1) / 2 - row) * angleStepV;
    
    // Convert to radians
    const radH = (angleH * Math.PI) / 180;
    const radV = (angleV * Math.PI) / 180;
    
    // Calculate 3D position (sensor at origin)
    const z = distMM * Math.cos(radV) * Math.cos(radH);
    const x = distMM * Math.cos(radV) * Math.sin(radH);
    const y = distMM * Math.sin(radV);
    
    return { x, y, z, angleH, angleV };
  }

  /**
   * Handle mouse drag for rotating the 3D view
   */
  function handleMouseDown(e) {
    isDragging = true;
    dragStartX = e.clientX;
    dragStartY = e.clientY;
  }

  function handleMouseMove(e) {
    if (!isDragging) return;
    
    const deltaX = e.clientX - dragStartX;
    const deltaY = e.clientY - dragStartY;
    
    rotationY += deltaX * 0.5;
    rotationX = Math.max(-90, Math.min(90, rotationX - deltaY * 0.5));
    
    dragStartX = e.clientX;
    dragStartY = e.clientY;
  }

  function handleMouseUp() {
    isDragging = false;
  }

  /**
   * Handle mouse wheel for zooming
   */
  function handleWheel(e) {
    e.preventDefault();
    const delta = e.deltaY * -0.001;
    zoom = Math.max(0.3, Math.min(3, zoom + delta));
  }

  /**
   * Get grid position for 3D view
   * @param {number} index - Zone index
   * @returns {Object} Grid position {row, col}
   */
  function getGridPosition(index) {
    const width = specifications?.width || 3;
    return {
      row: Math.floor(index / width),
      col: index % width
    };
  }

  /**
   * Start polling measurements
   */
  function startPolling() {
    if (!pollInterval && isPageVisible) {
      pollInterval = setInterval(fetchMeasurements, POLLING_INTERVALS.I2C_DISTANCE_SENSORS);
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

<div class="bg-white rounded-lg shadow p-4 border-l-4 border-blue-500">
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
  {:else if measurements.length > 0}
    <!-- Tab Navigation -->
    <div class="mt-3 border-b border-gray-200">
      <nav class="flex space-x-4">
        <button
          onclick={() => activeTab = 'grid'}
          class="px-4 py-2 text-sm font-medium border-b-2 transition-colors {activeTab === 'grid' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-600 hover:text-gray-800'}"
        >
          Grid View
        </button>
        <button
          onclick={() => activeTab = '3d'}
          class="px-4 py-2 text-sm font-medium border-b-2 transition-colors {activeTab === '3d' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-600 hover:text-gray-800'}"
        >
          3D View
        </button>
      </nav>
    </div>

    <!-- Tab Content -->
    <div class="mt-3">
      {#if activeTab === 'grid'}
        <!-- Grid View: Measurements in actual grid layout -->
        <div class="text-xs font-medium text-gray-600 mb-2">
          Distance Measurements ({measurements.length} zones)
        </div>
        <div 
          class="grid gap-2" 
          style="grid-template-columns: repeat({specifications?.width || 3}, 1fr); grid-template-rows: repeat({specifications?.height || 3}, 1fr);"
        >
          {#each measurements as measurement, index}
            <div class="p-3 border rounded {getConfidenceColor(measurement.confidence)}">
              <div class="text-xs font-medium opacity-75 mb-1">Z{index + 1}</div>
              <div class="text-lg font-bold">{formatDistance(measurement.distMM)}</div>
              <div class="text-xs mt-1">
                {(measurement.confidence * 100).toFixed(0)}%
              </div>
            </div>
          {/each}
        </div>
      {:else}
        <!-- 3D View: Distance visualization in sensor reference frame -->
        <div class="flex items-center justify-between mb-2">
          <div class="text-xs font-medium text-gray-600">
            3D FOV Projection (sensor-centered view)
          </div>
          <div class="flex gap-2 text-xs items-center">
            <button 
              onclick={() => zoom = Math.max(0.3, zoom - 0.2)}
              class="px-2 py-1 bg-gray-200 hover:bg-gray-300 rounded"
              title="Zoom Out"
            >
              −
            </button>
            <span class="text-gray-600 font-mono min-w-[60px] text-center">{(zoom * 100).toFixed(0)}%</span>
            <button 
              onclick={() => zoom = Math.min(3, zoom + 0.2)}
              class="px-2 py-1 bg-gray-200 hover:bg-gray-300 rounded"
              title="Zoom In"
            >
              +
            </button>
            <button 
              onclick={() => { rotationX = -60; rotationY = 180; zoom = 1.0; }}
              class="px-2 py-1 bg-gray-200 hover:bg-gray-300 rounded"
            >
              Reset View
            </button>
          </div>
        </div>
        
        <div 
          class="bg-gradient-to-b from-gray-800 to-gray-900 rounded-lg p-6 overflow-hidden relative select-none"
          style="cursor: {isDragging ? 'grabbing' : 'grab'};"
          onmousedown={handleMouseDown}
          onmousemove={handleMouseMove}
          onmouseup={handleMouseUp}
          onmouseleave={handleMouseUp}
          onwheel={handleWheel}
        >
          <div class="relative" style="perspective: 1200px; height: 500px;">
            <div 
              class="absolute inset-0 flex items-center justify-center"
              style="
                transform: translateZ({(zoom - 1) * 400}px) rotateX({rotationX}deg) rotateY({rotationY}deg);
                transform-style: preserve-3d;
                transition: transform 0.1s ease-out;
              "
            >
              <!-- Sensor at origin (center) as blue cross -->
              <!-- Vertical bar -->
              <div
                class="absolute bg-blue-500"
                style="
                  width: 4px;
                  height: 40px;
                  left: 50%;
                  top: 50%;
                  transform: translate(-50%, -50%) translateZ(-50px);
                  box-shadow: 0 0 15px rgba(59, 130, 246, 0.8);
                "
              ></div>
              <!-- Horizontal bar -->
              <div
                class="absolute bg-blue-500"
                style="
                  width: 40px;
                  height: 4px;
                  left: 50%;
                  top: 50%;
                  transform: translate(-50%, -50%) translateZ(-50px);
                  box-shadow: 0 0 15px rgba(59, 130, 246, 0.8);
                "
              ></div>

              <!-- Distance scale discs (ground plane every 500mm) -->
              {#each [500, 1000, 1500, 2000] as distance, idx}
                {@const radius = distance / 5}
                
                <!-- Ground disc ring -->
                <svg 
                  class="absolute"
                  style="
                    width: {radius * 2}px;
                    height: {radius * 2}px;
                    left: 50%;
                    top: 50%;
                    transform: translate(-50%, -50%) translateZ(-50px) rotateX(90deg);
                    transform-style: preserve-3d;
                    pointer-events: none;
                  "
                >
                  <circle 
                    cx="50%" 
                    cy="50%" 
                    r="{radius}" 
                    fill="transparent" 
                    stroke="rgba(200, 200, 200, 0.8)" 
                    stroke-width="2"
                    vector-effect="non-scaling-stroke"
                  />
                </svg>
                
                <!-- Distance label on the disc (horizontal) -->
                <div 
                  class="absolute text-white text-xs font-mono bg-gray-900 bg-opacity-70 px-2 py-1 rounded whitespace-nowrap"
                  style="
                    left: 50%;
                    top: 50%;
                    transform: translate(-50%, -50%) translateZ(-50px) rotateX(90deg) translateY({radius}px) rotateZ(180deg);
                    transform-style: preserve-3d;
                  "
                >
                  {distance}mm
                </div>
              {/each}

              <!-- Detection zones as curved rectangles -->
              {#each measurements as measurement, index}
                {@const pos3d = calculate3DPosition(index, measurement.distMM)}
                {@const scale = measurement.distMM / 5}
                {@const width = specifications?.width || 3}
                {@const height = specifications?.height || 3}
                {@const hFOV = specifications?.horizontalFOVDeg || 32}
                {@const vFOV = specifications?.verticalFOVDeg || 33}
                {@const zoneWidthAngle = hFOV / width}
                {@const zoneHeightAngle = vFOV / height}
                {@const zoneWidth = 2 * measurement.distMM * Math.tan((zoneWidthAngle * Math.PI / 180) / 2) / 5}
                {@const zoneHeight = 2 * measurement.distMM * Math.tan((zoneHeightAngle * Math.PI / 180) / 2) / 5}
                
                <!-- Zone rectangle (curved/oriented toward sensor) -->
                <div 
                  class="absolute border-2 rounded transition-all duration-300 {getConfidenceColor(measurement.confidence)}"
                  style="
                    width: {zoneWidth}px;
                    height: {zoneHeight}px;
                    transform: 
                      translateX({pos3d.x / 5}px)
                      translateY({-pos3d.y / 5}px)
                      translateZ({pos3d.z / 5}px)
                      rotateY({pos3d.angleH}deg)
                      rotateX({-pos3d.angleV}deg);
                    transform-style: preserve-3d;
                    opacity: 0.7;
                    box-shadow: 0 0 10px rgba(0, 0, 0, 0.5);
                  "
                >
                  <!-- Zone label -->
                  {#if measurement.distMM >= 750}
                    <div 
                      class="absolute inset-0 flex flex-col items-center justify-center text-xs font-bold pointer-events-none"
                      style="
                        transform: rotateY(180deg);
                        text-shadow: 0 0 4px rgba(0, 0, 0, 0.8);
                      "
                    >
                      <div class="text-white">Z{index + 1}</div>
                      <div class="text-white text-[10px] mt-1">{formatDistance(measurement.distMM)}</div>
                      <div class="text-white text-[9px] opacity-80">{(measurement.confidence * 100).toFixed(0)}%</div>
                    </div>
                  {/if}
                </div>
              {/each}
            </div>
          </div>
        </div>
        
        <div class="mt-2 text-xs text-gray-400 text-center italic">
          Drag to rotate • Scroll to zoom • Sensor (S) at center • Ground discs every 500mm
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
