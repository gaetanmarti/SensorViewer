<script>
  export let sensor;
  export let history = [];

  // Déterminer la couleur en fonction du type et de la valeur
  function getStatusColor(sensor) {
    const value = parseFloat(sensor.value);
    const unit = sensor.unit.toLowerCase();

    if (unit === "°c") {
      if (value >= 80) return "text-red-600";
      if (value >= 60) return "text-orange-500";
      return "text-green-600";
    }

    if (unit === "%") {
      if (value >= 90) return "text-red-600";
      if (value >= 70) return "text-orange-500";
      return "text-green-600";
    }

    return "text-gray-900";
  }

  // Générer les points du graphique sparkline
  function generateSparkline(history) {
    if (!history || history.length < 2) return "";

    const height = 40;
    const width = 200;
    const padding = 2;

    const min = Math.min(...history);
    const max = Math.max(...history);
    const range = max - min || 1; // Éviter division par zéro

    const points = history.map((value, index) => {
      const x = (index / (history.length - 1)) * width;
      const y = height - ((value - min) / range) * (height - padding * 2) - padding;
      return `${x},${y}`;
    });

    return points.join(" ");
  }

  $: statusColor = getStatusColor(sensor);
  $: sparklinePoints = generateSparkline(history);
</script>

<div class="bg-white rounded-lg shadow border border-gray-200 p-4 hover:shadow-md transition-shadow">
  <div class="flex justify-between items-start mb-2">
    <h3 class="text-sm font-medium text-gray-600 truncate pr-2" title={sensor.name}>
      {sensor.name}
    </h3>
    <span class="text-xs text-gray-400">{sensor.type}</span>
  </div>

  <div class="flex items-baseline mb-3">
    <span class="text-3xl font-bold {statusColor}">
      {sensor.value}
    </span>
    <span class="text-lg text-gray-500 ml-1">
      {sensor.unit}
    </span>
  </div>

  {#if history && history.length >= 2}
    <div class="mt-2">
      <svg viewBox="0 0 200 40" class="w-full h-10" preserveAspectRatio="none">
        <!-- Ligne de base -->
        <line
          x1="0"
          y1="40"
          x2="200"
          y2="40"
          stroke="#E5E7EB"
          stroke-width="1"
        />
        
        <!-- Polyline pour le graphique -->
        <polyline
          points={sparklinePoints}
          fill="none"
          stroke="#3B82F6"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        
        <!-- Points aux extrémités -->
        <circle
          cx={0}
          cy={sparklinePoints.split(" ")[0].split(",")[1]}
          r="2"
          fill="#3B82F6"
        />
        <circle
          cx={200}
          cy={sparklinePoints.split(" ")[sparklinePoints.split(" ").length - 1].split(",")[1]}
          r="2"
          fill="#3B82F6"
        />
      </svg>
      
      <div class="flex justify-between text-xs text-gray-400 mt-1">
        <span>-{(history.length - 1) * 5}s</span>
        <span>maintenant</span>
      </div>
    </div>
  {/if}
</div>
