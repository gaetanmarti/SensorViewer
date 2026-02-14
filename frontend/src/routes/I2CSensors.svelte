<script>
  /**
   * I2C Sensors Page
   * 
   * Scans and displays all detected I2C devices on the bus.
   * Automatically renders appropriate widgets based on device type:
   * - Distance sensors: DistanceSensor component with live measurements
   * - Thermal sensors: ThermalSensor component with thermal imaging
   * - Unknown/unsupported devices: UnknownSensor component with basic info
   * 
   * Features:
   * - Automatic device discovery on mount
   * - Manual refresh button
   * - Error handling and loading states
   * - Dynamic component rendering based on device type
   * 
   * @component
   */

  import { onMount } from 'svelte';
  import { API_BASE_URL, API_ENDPOINTS } from '../lib/config.js';
  import UnknownSensor from '../components/UnknownSensor.svelte';
  import DistanceSensor from '../components/DistanceSensor.svelte';
  import ThermalSensor from '../components/ThermalSensor.svelte';
  import HumanPresenceSensor from '../components/HumanPresenceSensor.svelte';

  let devices = $state([]);
  let loading = $state(true);
  let error = $state(null);
  let scanning = $state(false);

  /**
   * Scan I2C bus for connected devices
   * Fetches device list from backend API and updates the devices array
   */
  async function scanDevices() {
    scanning = true;
    error = null;
    
    try {
      const response = await fetch(`${API_BASE_URL}${API_ENDPOINTS.I2C_DEVICES}`);
      
      if (!response.ok) {
        throw new Error(`Failed to scan I2C devices: ${response.statusText}`);
      }
      
      const data = await response.json();
      
      if (data.ok && Array.isArray(data.devices)) {
        devices = data.devices;
        
        // Sort devices by address for consistent display
        devices.sort((a, b) => a.address - b.address);
      } else {
        throw new Error('Invalid response format from API');
      }
    } catch (err) {
      console.error('Error scanning I2C devices:', err);
      error = err.message;
      devices = [];
    } finally {
      loading = false;
      scanning = false;
    }
  }

  /**
   * Get appropriate component for device type
   * @param {string} type - Device type (e.g., "Distance", "Thermal", "HumanPresence")
   * @returns {*} Svelte component to render
   */
  function getComponentForDevice(type) {
    // Map device types to their respective components
    switch (type?.toLowerCase()) {
      case 'distance':
        return DistanceSensor;
      case 'thermal':
        return ThermalSensor;
      case 'humanpresence':
        return HumanPresenceSensor;
      default:
        return UnknownSensor;
    }
  }

  onMount(() => {
    scanDevices();
  });
</script>

<div>
  <div class="flex items-center justify-between mb-4">
    <h2 class="text-xl font-semibold text-gray-800">I2C Sensors</h2>
    <button
      onclick={scanDevices}
      disabled={scanning}
      class="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition-colors text-sm font-medium"
    >
      {scanning ? 'Scanning...' : 'Refresh Devices'}
    </button>
  </div>

  <!-- Error State -->
  {#if error}
    <div class="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded mb-4">
      <p class="font-medium">Error scanning I2C bus</p>
      <p class="text-sm mt-1">{error}</p>
    </div>
  {/if}

  <!-- Loading State -->
  {#if loading}
    <div class="flex items-center justify-center p-8">
      <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      <span class="ml-4 text-gray-600">Scanning I2C bus...</span>
    </div>
  {:else if devices.length === 0}
    <!-- No Devices Found -->
    <div class="bg-yellow-50 border border-yellow-200 text-yellow-700 px-4 py-3 rounded">
      <p class="font-medium">No I2C devices detected</p>
      <p class="text-sm mt-1">Make sure devices are properly connected and try refreshing.</p>
    </div>
  {:else}
    <!-- Device List -->
    <div class="space-y-4">
      <div class="text-sm text-gray-600 mb-2">
        Found {devices.length} device{devices.length !== 1 ? 's' : ''}
      </div>
      
      {#each devices as device (device.address)}
        {#if getComponentForDevice(device.type) === DistanceSensor}
          <DistanceSensor {device} />
        {:else if getComponentForDevice(device.type) === ThermalSensor}
          <ThermalSensor {device} />
        {:else if getComponentForDevice(device.type) === HumanPresenceSensor}
          <HumanPresenceSensor {device} />
        {:else}
          <UnknownSensor {device} />
        {/if}
      {/each}
    </div>
  {/if}
</div>
