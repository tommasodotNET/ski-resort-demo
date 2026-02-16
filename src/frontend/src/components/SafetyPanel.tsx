import { useEffect, useState } from 'react';
import { fetchSafety } from '../lib/data-api';
import { usePollingInterval } from '../lib/use-config';

interface Incident {
  incident_type: string;
  location: string;
  severity: string;
  timestamp?: string;
}

interface SafetyData {
  avalanche_risk_index: number;
  incident_reports: Incident[];
}

const riskColors: Record<string, string> = {
  low: '#22c55e',
  moderate: '#eab308',
  considerable: '#f97316',
  high: '#ef4444',
  extreme: '#dc2626',
};

export default function SafetyPanel() {
  const [safety, setSafety] = useState<SafetyData | null>(null);
  const pollingMs = usePollingInterval();

  useEffect(() => {
    const load = () => fetchSafety().then(setSafety).catch(console.error);
    load();
    const id = setInterval(load, pollingMs);
    return () => clearInterval(id);
  }, [pollingMs]);

  if (!safety) {
    return (
      <div className="rounded-2xl bg-slate-800/80 p-5 flex items-center justify-center text-slate-400">
        Loading safety…
      </div>
    );
  }

  const riskLevel = safety.avalanche_risk_index < 0.2 ? 'low'
    : safety.avalanche_risk_index < 0.4 ? 'moderate'
    : safety.avalanche_risk_index < 0.6 ? 'considerable'
    : safety.avalanche_risk_index < 0.8 ? 'high' : 'extreme';
  const riskColor = riskColors[riskLevel] ?? '#94a3b8';
  const riskPct = Math.min(safety.avalanche_risk_index * 100, 100);

  return (
    <div className="rounded-2xl bg-slate-800/80 p-5 flex flex-col gap-3">
      <h2 className="text-lg font-semibold text-amber-300">⚠️ Safety</h2>

      <div className="flex items-center gap-4">
        <div className="relative h-4 flex-1 rounded-full bg-slate-600 overflow-hidden">
          <div
            className="h-full rounded-full transition-all"
            style={{ width: `${riskPct}%`, backgroundColor: riskColor }}
          />
        </div>
        <span
          className="text-sm font-semibold capitalize px-2 py-0.5 rounded"
          style={{ color: riskColor }}
        >
          {riskLevel}
        </span>
      </div>
      <p className="text-xs text-slate-400">
        Avalanche Risk Index: {safety.avalanche_risk_index.toFixed(2)} / 1.0
      </p>

      {safety.incident_reports?.length > 0 && (
        <div className="flex flex-col gap-1.5 mt-1">
          <span className="text-xs text-slate-400 uppercase tracking-wide">
            Recent Incidents
          </span>
          {safety.incident_reports.slice(0, 4).map((inc, i) => (
            <div
              key={i}
              className="rounded-lg bg-slate-700/60 px-3 py-2 text-xs text-slate-300"
            >
              <span className="font-medium text-white">{inc.incident_type}</span>
              {' — '}
              {inc.location} ({inc.severity})
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
