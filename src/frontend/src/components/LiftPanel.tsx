import { useEffect, useState } from 'react';
import { fetchLifts } from '../lib/data-api';

interface Lift {
  name: string;
  status: string;
  queue_length: number;
  wait_time_minutes: number;
}

const statusColor: Record<string, string> = {
  open: 'bg-green-500',
  closed: 'bg-red-500',
  maintenance: 'bg-amber-500',
};

export default function LiftPanel() {
  const [lifts, setLifts] = useState<Lift[]>([]);

  useEffect(() => {
    const load = () =>
      fetchLifts()
        .then((d) => setLifts(Array.isArray(d) ? d : d.lifts ?? []))
        .catch(console.error);
    load();
    const id = setInterval(load, 3000);
    return () => clearInterval(id);
  }, []);

  if (!lifts.length) {
    return (
      <div className="rounded-2xl bg-slate-800/80 p-5 flex items-center justify-center text-slate-400">
        Loading lifts…
      </div>
    );
  }

  const maxQueue = Math.max(...lifts.map((l) => l.queue_length), 1);

  return (
    <div className="rounded-2xl bg-slate-800/80 p-5 flex flex-col gap-3 overflow-auto">
      <h2 className="text-lg font-semibold text-emerald-300">🚡 Lifts</h2>
      <div className="flex flex-col gap-2">
        {lifts.map((lift) => (
          <div
            key={lift.name}
            className="flex items-center gap-3 rounded-lg bg-slate-700/60 px-3 py-2 text-sm"
          >
            <span
              className={`h-2.5 w-2.5 rounded-full shrink-0 ${statusColor[lift.status?.toLowerCase()] ?? 'bg-gray-500'}`}
            />
            <span className="text-white font-medium min-w-[100px] truncate">
              {lift.name}
            </span>
            <div className="flex-1 h-2 rounded bg-slate-600 overflow-hidden">
              <div
                className="h-full rounded bg-emerald-400 transition-all"
                style={{ width: `${(lift.queue_length / maxQueue) * 100}%` }}
              />
            </div>
            <span className="text-slate-400 text-xs whitespace-nowrap">
              {lift.wait_time_minutes} min
            </span>
            <span
              className={`text-xs px-2 py-0.5 rounded-full capitalize ${statusColor[lift.status?.toLowerCase()] ?? 'bg-gray-500'} text-white`}
            >
              {lift.status}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}
