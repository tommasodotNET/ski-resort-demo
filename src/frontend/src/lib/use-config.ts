import { useEffect, useRef, useState } from 'react';

interface AppConfig {
  pollingIntervalMs: number;
}

const DEFAULT_CONFIG: AppConfig = { pollingIntervalMs: 10000 };
const CONFIG_REFRESH_MS = 30000; // re-read config every 30s

let cachedConfig: AppConfig = DEFAULT_CONFIG;
let lastFetch = 0;

async function fetchConfig(): Promise<AppConfig> {
  try {
    const res = await fetch('/config.json?t=' + Date.now());
    if (res.ok) {
      cachedConfig = { ...DEFAULT_CONFIG, ...(await res.json()) };
      lastFetch = Date.now();
    }
  } catch { /* keep cached */ }
  return cachedConfig;
}

export function usePollingInterval(): number {
  const [interval, setInterval_] = useState(cachedConfig.pollingIntervalMs);
  const timer = useRef<ReturnType<typeof setInterval>>();

  useEffect(() => {
    const refresh = async () => {
      const cfg = await fetchConfig();
      setInterval_(cfg.pollingIntervalMs);
    };

    // Fetch immediately if stale
    if (Date.now() - lastFetch > CONFIG_REFRESH_MS) {
      refresh();
    }

    timer.current = setInterval(refresh, CONFIG_REFRESH_MS);
    return () => clearInterval(timer.current);
  }, []);

  return interval;
}
