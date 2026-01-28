/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{svelte,js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        // Aquama primary color - Sky blue
        aquama: {
          DEFAULT: '#0EA5E9',
          light: '#38BDF8',
          dark: '#0284C7',
        },
        // Status colors
        status: {
          success: '#22C55E',
          warning: '#F97316',
          danger: '#EF4444',
        }
      },
    },
  },
  plugins: [require('daisyui')],
  daisyui: {
    themes: [
      {
        aquama: {
          "primary": "#0EA5E9",      // Aquama blue
          "secondary": "#0284C7",     // Darker aquama blue
          "accent": "#38BDF8",        // Lighter aquama blue
          "neutral": "#f5f5f5",
          "base-100": "#ffffff",
          "base-200": "#fafafa",
          "base-300": "#f0f0f0",
          "success": "#22C55E",       // Green
          "warning": "#F97316",       // Orange
          "error": "#EF4444",         // Red
        }
      }
    ],
    base: true,
    styled: true,
  },
}
