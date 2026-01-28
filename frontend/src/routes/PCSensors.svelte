<script>
  import { onMount, onDestroy } from "svelte";
  import { API_BASE_URL } from "../lib/config.js";
  import SensorCard from "../components/SensorCard.svelte";

  let sensors = [];
  let loading = true;
  let error = null;
  let intervalId = null;
  
  // Historique des valeurs pour chaque capteur (max 30 points = 150 secondes)
  let sensorHistory = {};
  const MAX_HISTORY = 30;

  async function fetchSensors() {
    try {
      const response = await fetch(`${API_BASE_URL}/api/sensors`);
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      const data = await response.json();
      sensors = data.sensors || [];
      
      // Mettre à jour l'historique pour chaque capteur
      sensors.forEach(sensor => {
        const value = parseFloat(sensor.value);
        
        if (!sensorHistory[sensor.name]) {
          // Initialiser avec la première valeur dupliquée pour avoir au moins 2 points
          sensorHistory[sensor.name] = [value, value];
        } else {
          sensorHistory[sensor.name].push(value);
          
          // Limiter la taille de l'historique
          if (sensorHistory[sensor.name].length > MAX_HISTORY) {
            sensorHistory[sensor.name].shift();
          }
        }
      });
      
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
      {#each sensors as sensor (sensor.name)}
        <SensorCard 
          sensor={sensor} 
          history={sensorHistory[sensor.name] || []}
        />
      {/each}
    </div>
  {/if}
</div>
