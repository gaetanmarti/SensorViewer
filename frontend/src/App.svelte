<script>
  import PCSensors from "./routes/PCSensors.svelte";
  import I2CSensors from "./routes/I2CSensors.svelte";

  let activeTab = "pc";
</script>

<div class="min-h-screen bg-gray-50">
  <header class="bg-white shadow">
    <div class="container mx-auto px-6 py-4">
      <h1 class="text-2xl font-bold text-gray-800">Sensor Viewer</h1>
    </div>
  </header>

  <nav class="bg-white border-b border-gray-200">
    <div class="container mx-auto px-6">
      <div class="flex space-x-4">
        <button
          class="px-4 py-3 font-medium border-b-2 transition-colors {activeTab === 'pc' 
            ? 'border-blue-500 text-blue-600' 
            : 'border-transparent text-gray-600 hover:text-gray-800 hover:border-gray-300'}"
          on:click={() => activeTab = 'pc'}
        >
          PC Sensors
        </button>
        <button
          class="px-4 py-3 font-medium border-b-2 transition-colors {activeTab === 'i2c' 
            ? 'border-blue-500 text-blue-600' 
            : 'border-transparent text-gray-600 hover:text-gray-800 hover:border-gray-300'}"
          on:click={() => activeTab = 'i2c'}
        >
          i2c Sensors
        </button>
      </div>
    </div>
  </nav>

  <main class="container mx-auto px-6 py-8">
    {#if activeTab === 'pc'}
      <PCSensors />
    {:else if activeTab === 'i2c'}
      <I2CSensors />
    {/if}
  </main>
</div>
