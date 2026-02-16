async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json();
}

export const fetchWeather = () => get<any>('/api/current-state/weather');
export const fetchLifts = () => get<any>('/api/current-state/lifts');
export const fetchSafety = () => get<any>('/api/current-state/safety');
export const fetchSlopes = () => get<any>('/api/current-state/slopes');
