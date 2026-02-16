import { useEffect, useState } from 'react';
import { fetchLifts, fetchSlopes } from '../lib/data-api';
import { usePollingInterval } from '../lib/use-config';

interface Lift {
  lift_id: string;
  name: string;
  status: string;
  queue_length: number;
  wait_time_minutes: number;
  serves_slopes: string[];
}

interface Slope {
  slope_id: string;
  name: string;
  difficulty: string;
  is_open: boolean;
  snow_depth_cm: number;
  groomed: boolean;
  served_by_lift_id: string;
}

const statusColor: Record<string, string> = {
  open: 'bg-green-500',
  closed: 'bg-red-500',
  maintenance: 'bg-amber-500',
};

const difficultyStyle: Record<string, { color: string; label: string }> = {
  green: { color: 'bg-green-500', label: '●' },
  blue: { color: 'bg-blue-500', label: '■' },
  red: { color: 'bg-red-500', label: '◆' },
  black: { color: 'bg-black border border-white', label: '◆◆' },
};

export default function SlopesAndLiftsPanel() {
  const [lifts, setLifts] = useState<Lift[]>([]);
  const [slopes, setSlopes] = useState<Slope[]>([]);
  const pollingMs = usePollingInterval();

  useEffect(() => {
    const load = () => {
      fetchLifts()
        .then((d) => setLifts(Array.isArray(d) ? d : d.lifts ?? []))
        .catch(console.error);
      fetchSlopes()
        .then((d) => setSlopes(Array.isArray(d) ? d : d.slopes ?? []))
        .catch(console.error);
    };
    load();
    const id = setInterval(load, pollingMs);
    return () => clearInterval(id);
  }, [pollingMs]);

  if (!lifts.length && !slopes.length) {
    return (
      <div className="rounded-2xl bg-slate-800/80 p-5 flex items-center justify-center text-slate-400">
        Loading slopes & lifts…
      </div>
    );
  }

  // Build a map of lift_id → slopes
  const slopesByLift = new Map<string, Slope[]>();
  for (const slope of slopes) {
    const key = slope.served_by_lift_id;
    if (!slopesByLift.has(key)) slopesByLift.set(key, []);
    slopesByLift.get(key)!.push(slope);
  }

  return (
    <div className="rounded-2xl bg-slate-800/80 p-5 flex flex-col gap-3 overflow-auto">
      <h2 className="text-lg font-semibold text-violet-300">🏔️ Slopes & Lifts</h2>
      <div className="flex flex-col gap-3">
        {lifts.map((lift) => {
          const liftSlopes = slopesByLift.get(lift.lift_id) ?? [];
          return (
            <div key={lift.lift_id} className="rounded-lg bg-slate-700/40 overflow-hidden">
              {/* Lift header */}
              <div className="flex items-center gap-3 px-3 py-2 bg-slate-700/60">
                <span className="text-base">🚡</span>
                <span className="text-white font-medium text-sm flex-1 truncate">
                  {lift.name}
                </span>
                <span className="text-slate-400 text-xs whitespace-nowrap">
                  🕐 {lift.wait_time_minutes} min
                </span>
                <span className="text-slate-400 text-xs whitespace-nowrap">
                  👥 {lift.queue_length}
                </span>
                <span
                  className={`text-xs px-2 py-0.5 rounded-full capitalize ${statusColor[lift.status?.toLowerCase()] ?? 'bg-gray-500'} text-white`}
                >
                  {lift.status}
                </span>
              </div>

              {/* Slopes served by this lift */}
              {liftSlopes.length > 0 && (
                <div className="flex flex-col gap-px bg-slate-800/30">
                  {liftSlopes.map((slope) => {
                    const diff = difficultyStyle[slope.difficulty?.toLowerCase()] ?? {
                      color: 'bg-gray-500',
                      label: '?',
                    };
                    return (
                      <div
                        key={slope.slope_id}
                        className="flex items-center gap-3 px-4 pl-8 py-1.5 text-sm"
                      >
                        <span
                          className={`h-4 w-4 rounded-full flex items-center justify-center text-[9px] shrink-0 ${diff.color} text-white`}
                        >
                          {diff.label}
                        </span>
                        <span className="text-slate-200 min-w-[100px] truncate">
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
                          className={`ml-auto text-xs px-2 py-0.5 rounded-full ${slope.is_open ? 'bg-green-500' : 'bg-red-500'} text-white`}
                        >
                          {slope.is_open ? 'Open' : 'Closed'}
                        </span>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
