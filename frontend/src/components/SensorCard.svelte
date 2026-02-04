<script>
  import { onMount, onDestroy } from "svelte";
  import { Chart } from "chart.js/auto";

  export let sensor;
  export let history = [];

  let canvas;
  let chart;

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

  function getChartColor(unit, value) {
    const numValue = parseFloat(value);
    
    if (unit === "Temperature") {
      if (numValue >= 80) return "rgba(220, 38, 38, 0.8)";
      if (numValue >= 60) return "rgba(249, 115, 22, 0.8)";
      return "rgba(22, 163, 74, 0.8)";
    }
    
    if (unit === "Percent") {
      if (numValue >= 90) return "rgba(220, 38, 38, 0.8)";
      if (numValue >= 70) return "rgba(249, 115, 22, 0.8)";
      return "rgba(22, 163, 74, 0.8)";
    }
    
    return "rgba(59, 130, 246, 0.8)";
  }

  function updateChart() {
    if (!chart || !canvas) return;

    const color = getChartColor(sensor.unit, sensor.value);

    chart.data.labels = Array(history.length).fill("");
    chart.data.datasets[0].borderColor = color;
    chart.data.datasets[0].backgroundColor = color.replace('0.8', '0.2');
    chart.data.datasets[0].data = [...history]; // Clone array to trigger update
    chart.update('none'); // Update without animation for smooth real-time feel
  }

  onMount(() => {
    if (!canvas) return;

    const ctx = canvas.getContext("2d");
    const color = getChartColor(sensor.unit, sensor.value);

    chart = new Chart(ctx, {
      type: "line",
      data: {
        labels: Array(history.length).fill(""),
        datasets: [{
          data: [...history],
          borderColor: color,
          backgroundColor: color.replace('0.8', '0.2'),
          borderWidth: 2,
          tension: 0.4,
          pointRadius: 0,
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
              label: (context) => `${context.parsed.y.toFixed(2)} ${getUnitSymbol(sensor.unit)}`
            }
          }
        },
        scales: {
          x: { display: false },
          y: {
            display: true,
            position: 'right',
            beginAtZero: sensor.unit === "Percent",
            min: sensor.unit === "Percent" ? 0 : undefined,
            max: sensor.unit === "Percent" ? 100 : undefined,
            ticks: {
              callback: (value) => `${value}${getUnitSymbol(sensor.unit)}`,
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
  });

  onDestroy(() => {
    if (chart) {
      chart.destroy();
    }
  });

  $: if (chart && history && history.length > 0) {
    updateChart();
  }
</script>

<div class="bg-white rounded-lg shadow p-4 border border-gray-200 hover:shadow-md transition-shadow">
  <h3 class="text-sm font-medium text-gray-600 mb-2">{sensor.name}</h3>
  
  <div class="flex items-center justify-between mb-3">
    <div class="flex items-baseline space-x-1">
      <span class="text-3xl font-bold {getColorClass(sensor.unit, sensor.value)}">
        {sensor.value}
      </span>
      <span class="text-lg text-gray-500">
        {getUnitSymbol(sensor.unit)}
      </span>
    </div>
  </div>

  <div class="h-16">
    <canvas bind:this={canvas}></canvas>
  </div>
</div>
