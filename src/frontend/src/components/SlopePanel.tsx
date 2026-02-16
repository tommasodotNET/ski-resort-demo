import { useEffect, useState } from 'react';
import { fetchSlopes } from '../lib/data-api';
import { usePollingInterval } from '../lib/use-config';

interface Slope {
  name: string;
  difficulty: string;
  is_open: boolean;
  snow_depth_cm: number;
  groomed: boolean;
}

const difficultyStyle: Record<string, { color: string; label: string }> = {
  green: { color: 'bg-green-500', label: '●' },
  blue: { color: 'bg-blue-500', label: '■' },
  red: { color: 'bg-red-500', label: '◆' },
  black: { color: 'bg-black border border-white', label: '◆◆' },
};

export default function SlopePanel() {
  const [slopes, setSlopes] = useState<Slope[]>([]);
  const pollingMs = usePollingInterval();

  useEffect(() => {
    const load = () =>
      fetchSlopes()
        .then((d) => setSlopes(Array.isArray(d) ? d : d.slopes ?? []))
        .catch(console.error);
    load();
    const id = setInterval(load, pollingMs);
    return () => clearInterval(id);
  }, [pollingMs]);

  if (!slopes.length) {
    return (
      <div className="rounded-2xl bg-slate-800/80 p-5 flex items-center justify-center text-slate-400">
        Loading slopes…
      </div>
    );
  }

  return (
    <div className="rounded-2xl bg-slate-800/80 p-5 flex flex-col gap-3 overflow-auto">
      <h2 className="text-lg font-semibold text-rose-300">⛷️ Slopes</h2>
      <div className="flex flex-col gap-2">
        {slopes.map((slope) => {
          const diff = difficultyStyle[slope.difficulty?.toLowerCase()] ?? {
            color: 'bg-gray-500',
            label: '?',
          };
          const open = slope.is_open;
          return (
            <div
              key={slope.name}
              className="flex items-center gap-3 rounded-lg bg-slate-700/60 px-3 py-2 text-sm"
            >
              <span
                className={`h-5 w-5 rounded-full flex items-center justify-center text-[10px] shrink-0 ${diff.color} text-white`}
              >
                {diff.label}
              </span>
              <span className="text-white font-medium min-w-[100px] truncate">
                {slope.name}
              </span>
              <span className="text-slate-400 text-xs">
                {slope.snow_depth_cm} cm
              </span>
              {slope.groomed && (
                <span className="text-xs px-1.5 py-0.5 rounded bg-sky-700 text-sky-200">
                  Groomed
                </span>
              )}
              <span
                className={`ml-auto text-xs px-2 py-0.5 rounded-full ${open ? 'bg-green-500' : 'bg-red-500'} text-white`}
              >
                {open ? 'Open' : 'Closed'}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
