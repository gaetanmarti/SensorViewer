<script>
  /**
   * UnknownSensor Component
   * 
   * Displays basic information for unrecognized or unsupported I2C sensor devices.
   * Shows device address, name, and type in a simple card layout.
   * 
   * @component
   * @prop {Object} device - The I2C device object
   * @prop {number} device.address - I2C address in decimal format
   * @prop {string} device.name - Device name or "Unknown I2C Device"
   * @prop {string} device.type - Device type (e.g., "Distance", "Temperature")
   */

  let { device } = $props();
</script>

<div class="bg-white rounded-lg shadow p-4 border-l-4 border-gray-400">
  <div class="flex items-start justify-between">
    <div class="flex-1">
      <h3 class="text-lg font-semibold text-gray-800 mb-1">{device.name}</h3>
      <div class="space-y-1 text-sm text-gray-600">
        <div>
          <span class="font-medium">Address:</span>
          <span class="ml-2 font-mono">0x{device.address.toString(16).toUpperCase()} ({device.address})</span>
        </div>
        <div>
          <span class="font-medium">Type:</span>
          <span class="ml-2">{device.type}</span>
        </div>
      </div>
    </div>
    <div class="ml-4">
      <span class="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-gray-100 text-gray-700">
        Unknown
      </span>
    </div>
  </div>
  
  <div class="mt-3 p-2 bg-gray-50 rounded text-xs text-gray-500 italic">
    No widget available for this sensor type yet.
  </div>
</div>
