<script>
  import { onMount, onDestroy } from "svelte";
  import { API_BASE_URL } from "../lib/config.js";

  let sensors = [];
  let loading = true;
  let error = null;
  let intervalId = null;

  async function fetchSensors() {
    try {
      const response = await fetch(`${API_BASE_URL}/api/sensors`);
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      const data = await response.json();
      sensors = data.sensors || [];
      error = null;
    } catch (e) {
      error = e.message;
      console.error("Erreur lors de la récupération des capteurs:", e);
    } finally {
      loading = false;
    }
  }

  onMount(() => {
    // Première récupération immédiate
    fetchSensors();
    
    // Ensuite toutes les 5 secondes
    intervalId = setInterval(fetchSensors, 5000);
  });

  onDestroy(() => {
    if (intervalId) {
      clearInterval(intervalId);
    }
  });

  function getUnitSymbol(unit) {
    switch (unit) {
      case "Temperature":
        return "°C";
      case "Percent":
        return "%";
      default:
        return "";
    }
  }

  function getColorClass(unit, value) {
    const numValue = parseFloat(value);
    
    if (unit === "Temperature") {
      if (numValue >= 80) return "text-red-600";
      if (numValue >= 60) return "text-orange-500";
      return "text-green-600";
    }
    
    if (unit === "Percent") {
      if (numValue >= 90) return "text-red-600";
      if (numValue >= 70) return "text-orange-500";
      return "text-green-600";
    }
    
    return "text-gray-800";
  }
</script>

<div>
  <h2 class="text-xl font-semibold text-gray-800 mb-4">PC Sensors</h2>

  {#if loading}
    <div class="flex items-center justify-center py-12">
      <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
    </div>
  {:else if error}
    <div class="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
      <p class="font-medium">Erreur de connexion</p>
      <p class="text-sm">{error}</p>
    </div>
  {:else if sensors.length === 0}
    <div class="bg-gray-50 border border-gray-200 text-gray-600 px-4 py-3 rounded text-center">
      Aucun capteur disponible
    </div>
  {:else}
    <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      {#each sensors as sensor}
        <div class="bg-white rounded-lg shadow p-4 border border-gray-200 hover:shadow-md transition-shadow">
          <h3 class="text-sm font-medium text-gray-600 mb-2">{sensor.name}</h3>
          <div class="flex items-baseline space-x-1">
            <span class="text-3xl font-bold {getColorClass(sensor.unit, sensor.value)}">
              {sensor.value}
            </span>
            <span class="text-lg text-gray-500">
              {getUnitSymbol(sensor.unit)}
            </span>
          </div>
        </div>
      {/each}
    </div>
  {/if}
</div>
