import { useEffect, useState } from 'react';
import { fetchWeather } from '../lib/data-api';
import { usePollingInterval } from '../lib/use-config';
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  Cell,
} from 'recharts';

interface WeatherData {
  temperature: number;
  wind_speed: number;
  snow_intensity: number;
  visibility: number;
}

export default function WeatherPanel() {
  const [weather, setWeather] = useState<WeatherData | null>(null);
  const pollingMs = usePollingInterval();

  useEffect(() => {
    const load = () => fetchWeather().then(setWeather).catch(console.error);
    load();
    const id = setInterval(load, pollingMs);
    return () => clearInterval(id);
  }, [pollingMs]);

  if (!weather) {
    return (
      <div className="rounded-2xl bg-slate-800/80 p-5 flex items-center justify-center text-slate-400">
        Loading weather…
      </div>
    );
  }

  const chartData = [
    { name: 'Temp (°C)', value: weather.temperature, color: '#60a5fa' },
    { name: 'Wind (km/h)', value: weather.wind_speed, color: '#34d399' },
    { name: 'Snow (cm/h)', value: weather.snow_intensity, color: '#c084fc' },
    { name: 'Visibility (m)', value: weather.visibility / 10, color: '#fbbf24' },
  ];

  return (
    <div className="rounded-2xl bg-slate-800/80 p-5 flex flex-col gap-3">
      <h2 className="text-lg font-semibold text-sky-300">🌨️ Weather</h2>
      <div className="grid grid-cols-2 gap-3 text-sm">
        <Stat label="Temperature" value={`${weather.temperature.toFixed(1)}°C`} />
        <Stat label="Wind Speed" value={`${weather.wind_speed.toFixed(1)} km/h`} />
        <Stat label="Snow Intensity" value={`${weather.snow_intensity.toFixed(1)} cm/h`} />
        <Stat label="Visibility" value={`${weather.visibility.toFixed(0)} m`} />
      </div>
      <div className="h-36">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={chartData}>
            <XAxis dataKey="name" tick={{ fill: '#94a3b8', fontSize: 11 }} />
            <YAxis hide />
            <Tooltip
              contentStyle={{ background: '#1e293b', border: 'none', borderRadius: 8 }}
              labelStyle={{ color: '#e2e8f0' }}
            />
            <Bar dataKey="value" radius={[4, 4, 0, 0]}>
              {chartData.map((d, i) => (
                <Cell key={i} fill={d.color} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg bg-slate-700/60 px-3 py-2">
      <div className="text-slate-400 text-xs">{label}</div>
      <div className="text-white font-medium">{value}</div>
    </div>
  );
}
