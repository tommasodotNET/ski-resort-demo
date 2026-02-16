import WeatherPanel from './components/WeatherPanel';
import LiftPanel from './components/LiftPanel';
import SafetyPanel from './components/SafetyPanel';
import SlopePanel from './components/SlopePanel';
import ChatPanel from './components/ChatPanel';

export default function App() {
  return (
    <div className="flex flex-col h-screen overflow-hidden">
      <header className="flex items-center gap-3 px-6 py-4 border-b border-slate-700/60 bg-slate-900/80 backdrop-blur">
        <span className="text-2xl">🏔️</span>
        <h1 className="text-xl font-bold text-white tracking-tight">
          AlpineAI
          <span className="text-slate-400 font-normal ml-2 text-base">
            Ski Resort Dashboard
          </span>
        </h1>
      </header>

      <div className="flex flex-1 overflow-hidden">
        <main className="flex-[2] p-4 overflow-y-auto grid grid-cols-1 md:grid-cols-2 gap-4 auto-rows-min">
          <WeatherPanel />
          <LiftPanel />
          <SafetyPanel />
          <SlopePanel />
        </main>

        <aside className="flex-1 p-4 pl-0 min-w-[320px] max-w-[480px]">
          <ChatPanel />
        </aside>
      </div>
    </div>
  );
}
