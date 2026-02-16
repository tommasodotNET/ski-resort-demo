import { useState, useRef, useEffect, type FormEvent } from 'react';
import { sendMessageStream, resetClient } from '../lib/a2a-client';

interface ChatMessage {
  role: 'user' | 'agent';
  text: string;
}

export default function ChatPanel() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [contextId, setContextId] = useState<string | undefined>(undefined);
  const endRef = useRef<HTMLDivElement>(null);
  const accRef = useRef('');

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  function handleNewConversation() {
    setMessages([]);
    setContextId(undefined);
    resetClient();
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const text = input.trim();
    if (!text || loading) return;

    setInput('');
    setMessages((prev) => [...prev, { role: 'user', text }]);
    setLoading(true);
    accRef.current = '';

    setMessages((prev) => [...prev, { role: 'agent', text: '' }]);

    try {
      for await (const event of sendMessageStream(text, contextId)) {
        if (event.contextId) {
          setContextId(event.contextId);
        }
        if (event.content) {
          accRef.current += event.content;
          const snapshot = accRef.current;
          setMessages((prev) => {
            const next = [...prev];
            next[next.length - 1] = { role: 'agent', text: snapshot };
            return next;
          });
        }
      }
      if (!accRef.current) {
        setMessages((prev) => {
          const next = [...prev];
          next[next.length - 1] = {
            role: 'agent',
            text: '(No response from advisor)',
          };
          return next;
        });
      }
    } catch (err) {
      console.error('A2A error', err);
      setMessages((prev) => {
        const next = [...prev];
        next[next.length - 1] = {
          role: 'agent',
          text: `Error: ${err instanceof Error ? err.message : 'Connection failed'}`,
        };
        return next;
      });
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="rounded-2xl bg-slate-800/80 flex flex-col h-full">
      <div className="flex items-center justify-between px-5 pt-5 pb-3">
        <h2 className="text-lg font-semibold text-violet-300">
          🤖 AI Advisor
        </h2>
        <button
          onClick={handleNewConversation}
          disabled={loading}
          className="text-xs px-3 py-1.5 rounded-lg bg-slate-700 text-slate-300 hover:bg-slate-600 hover:text-white disabled:opacity-50 transition-colors"
        >
          + New Chat
        </button>
      </div>

      <div className="flex-1 overflow-y-auto px-5 space-y-3">
        {messages.length === 0 && (
          <p className="text-slate-500 text-sm pt-8 text-center">
            Ask the AlpineAI advisor about conditions, recommendations, or
            safety info.
          </p>
        )}
        {messages.map((msg, i) => (
          <div
            key={i}
            className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}
          >
            <div
              className={`max-w-[85%] rounded-xl px-4 py-2 text-sm whitespace-pre-wrap ${
                msg.role === 'user'
                  ? 'bg-violet-600 text-white'
                  : 'bg-slate-700 text-slate-200'
              }`}
            >
              {msg.text || (loading && i === messages.length - 1 ? 'thinking…' : '')}
            </div>
          </div>
        ))}
        <div ref={endRef} />
      </div>

      <form
        onSubmit={handleSubmit}
        className="flex gap-2 px-5 py-4 border-t border-slate-700"
      >
        <input
          className="flex-1 rounded-lg bg-slate-700 px-4 py-2 text-sm text-white placeholder-slate-400 outline-none focus:ring-2 focus:ring-violet-500"
          placeholder="Ask the AI advisor…"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          disabled={loading}
        />
        <button
          type="submit"
          disabled={loading}
          className="rounded-lg bg-violet-600 px-4 py-2 text-sm font-medium text-white hover:bg-violet-500 disabled:opacity-50 transition-colors"
        >
          Send
        </button>
      </form>
    </div>
  );
}
