import { useState, useRef, useEffect, useCallback, type FormEvent } from 'react';
import {
  sendMessageStream,
  resetClient,
  type AdvisorArchitecture,
} from '../lib/responses-client';
import type { VoiceTranscript } from '../lib/VoiceSession';
import VoiceButton from './VoiceButton';

interface ChatMessage {
  role: 'user' | 'agent';
  text: string;
  source?: 'chat' | 'voice';
  /** True while the message is still receiving streaming partial transcripts */
  partial?: boolean;
}

export default function ChatPanel() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [architecture, setArchitecture] = useState<AdvisorArchitecture>('a2a');
  const [contextId, setContextId] = useState<string | undefined>(undefined);
  const contextIdRef = useRef<string | undefined>(undefined);
  const endRef = useRef<HTMLDivElement>(null);
  const accRef = useRef('');
  const initRef = useRef(false);
  /** Tracks the messages-array index of the active streaming bubble for each voice role */
  const partialIndexRef = useRef<{ user?: number; agent?: number }>({});
  /** Accumulated text for the active voice bubble — source of truth, avoids double-append in Strict Mode */
  const voiceAccRef = useRef<{ user: string; agent: string }>({ user: '', agent: '' });

  // Keep ref in sync with state so voice callbacks always have the latest value
  useEffect(() => { contextIdRef.current = contextId; }, [contextId]);

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // On mount, send an empty greeting to get the agent to introduce itself and capture the contextId
  useEffect(() => {
    if (initRef.current) return;
    initRef.current = true;
    (async () => {
      setLoading(true);
      setMessages([{ role: 'agent', text: '', source: 'chat' }]);
      accRef.current = '';
      try {
        for await (const event of sendMessageStream('Greet the user and briefly offer ski resort advisory help.', architecture)) {
          if (event.contextId) {
            setContextId(event.contextId);
          }
          if (event.content) {
            accRef.current += event.content;
            const snapshot = accRef.current;
            setMessages([{ role: 'agent', text: snapshot, source: 'chat' }]);
          }
        }
        if (!accRef.current) {
          setMessages([{ role: 'agent', text: 'Hello! How can I help you today?', source: 'chat' }]);
        }
      } catch {
        setMessages([{ role: 'agent', text: 'Hello! How can I help you today?', source: 'chat' }]);
      } finally {
        setLoading(false);
      }
    })();
  }, [architecture]);

  function handleNewConversation() {
    setMessages([]);
    setContextId(undefined);
    initRef.current = false;
    partialIndexRef.current = {};
    voiceAccRef.current = { user: '', agent: '' };
    resetClient();
    // Re-trigger greeting
    (async () => {
      setLoading(true);
      setMessages([{ role: 'agent', text: '', source: 'chat' }]);
      accRef.current = '';
      try {
        for await (const event of sendMessageStream('Greet the user and briefly offer ski resort advisory help.', architecture)) {
          if (event.contextId) {
            setContextId(event.contextId);
          }
          if (event.content) {
            accRef.current += event.content;
            const snapshot = accRef.current;
            setMessages([{ role: 'agent', text: snapshot, source: 'chat' }]);
          }
        }
        if (!accRef.current) {
          setMessages([{ role: 'agent', text: 'Hello! How can I help you today?', source: 'chat' }]);
        }
      } catch {
        setMessages([{ role: 'agent', text: 'Hello! How can I help you today?', source: 'chat' }]);
      } finally {
        setLoading(false);
      }
    })();
  }

  const handleVoiceTranscript = useCallback((transcript: VoiceTranscript) => {
    const msgRole = transcript.role === 'user' ? 'user' as const : 'agent' as const;

    const currentIdx = partialIndexRef.current[msgRole];

    if (transcript.isFinal) {
      // Final message carries the complete text — just seal the existing bubble (or create one)
      partialIndexRef.current[msgRole] = undefined;
      voiceAccRef.current[msgRole] = '';

      setMessages((prev) => {
        const hasOpenBubble = currentIdx !== undefined && currentIdx < prev.length && prev[currentIdx].partial;
        if (hasOpenBubble) {
          // Keep the text we already accumulated — ignore the final's text to avoid duplication
          const next = [...prev];
          next[currentIdx] = { ...next[currentIdx], partial: false };
          return next;
        }
        // No open bubble — use the final text as-is
        return [...prev, { role: msgRole, text: transcript.text, source: 'voice', partial: false }];
      });
      return;
    }

    // Partial delta — append to accumulator, write snapshot to state
    voiceAccRef.current[msgRole] += transcript.text;
    const snapshot = voiceAccRef.current[msgRole];

    setMessages((prev) => {
      const hasOpenBubble = currentIdx !== undefined && currentIdx < prev.length && prev[currentIdx].partial;
      if (hasOpenBubble) {
        const next = [...prev];
        next[currentIdx] = { role: msgRole, text: snapshot, source: 'voice', partial: true };
        return next;
      }
      partialIndexRef.current[msgRole] = prev.length;
      return [...prev, { role: msgRole, text: snapshot, source: 'voice', partial: true }];
    });
  }, []);

  /** When the server clears audio (agent interrupted), discard any stale agent partial */
  const handleClearAudio = useCallback(() => {
    const agentIdx = partialIndexRef.current.agent;
    if (agentIdx === undefined) return;
    partialIndexRef.current.agent = undefined;
    voiceAccRef.current.agent = '';
    setMessages((prev) => {
      if (agentIdx >= prev.length || !prev[agentIdx].partial) return prev;
      return prev.filter((_, i) => i !== agentIdx);
    });
  }, []);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const text = input.trim();
    if (!text || loading) return;

    setInput('');
    setMessages((prev) => [...prev, { role: 'user', text, source: 'chat' }]);
    setLoading(true);
    accRef.current = '';

    setMessages((prev) => [...prev, { role: 'agent', text: '', source: 'chat' }]);

    try {
      for await (const event of sendMessageStream(text, architecture, contextId)) {
        if (event.contextId) {
          setContextId(event.contextId);
        }
        if (event.content) {
          accRef.current += event.content;
          const snapshot = accRef.current;
          setMessages((prev) => {
            const next = [...prev];
            next[next.length - 1] = { role: 'agent', text: snapshot, source: 'chat' };
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
            source: 'chat',
          };
          return next;
        });
      }
    } catch (err) {
      console.error('Responses API error', err);
      setMessages((prev) => {
        const next = [...prev];
        next[next.length - 1] = {
          role: 'agent',
          text: `Error: ${err instanceof Error ? err.message : 'Connection failed'}`,
          source: 'chat',
        };
        return next;
      });
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="rounded-2xl bg-slate-800/80 flex min-w-0 flex-col h-full">
      <div className="flex min-w-0 flex-wrap items-center justify-between gap-3 px-4 sm:px-5 pt-5 pb-3">
        <h2 className="text-lg font-semibold text-violet-300">
          🤖 AI Advisor
        </h2>
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <select
            value={architecture}
            onChange={(event) => {
              setArchitecture(event.target.value as AdvisorArchitecture);
              setMessages([]);
              setContextId(undefined);
              initRef.current = false;
              resetClient();
            }}
            disabled={loading}
            className="rounded-lg bg-slate-700 px-2 py-1.5 text-xs text-slate-200"
            aria-label="Advisor architecture"
          >
            <option value="a2a">A2A agents</option>
            <option value="skill">MCP skills</option>
          </select>
          <VoiceButton
            key={architecture}
            onTranscript={handleVoiceTranscript}
            onConversationId={setContextId}
            onClearAudio={handleClearAudio}
            disabled={loading}
            conversationId={contextId}
            architecture={architecture}
          />
          <button
            onClick={handleNewConversation}
            disabled={loading}
            className="text-xs px-3 py-1.5 rounded-lg bg-slate-700 text-slate-300 hover:bg-slate-600 hover:text-white disabled:opacity-50 transition-colors"
          >
            + New Chat
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-4 sm:px-5 space-y-3">
        {messages.length === 0 && (
          <p className="text-slate-500 text-sm pt-8 text-center">
            Ask the AlpineAI advisor about conditions, recommendations, or
            safety info — by text or voice.
          </p>
        )}
        {messages.map((msg, i) => (
          <div
            key={i}
              className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}
            >
              <div
                className={`max-w-[85%] min-w-0 break-words rounded-xl px-4 py-2 text-sm whitespace-pre-wrap ${
                msg.role === 'user'
                  ? 'bg-violet-600 text-white'
                  : 'bg-slate-700 text-slate-200'
              }`}
            >
              {msg.source === 'voice' && (
                <span className="text-[10px] opacity-60 mr-1">🎙️</span>
              )}
              {msg.text || (loading && i === messages.length - 1 ? 'thinking…' : '')}
            </div>
          </div>
        ))}
        <div ref={endRef} />
      </div>

      <form
        onSubmit={handleSubmit}
        className="flex min-w-0 gap-2 px-4 sm:px-5 py-4 border-t border-slate-700"
      >
        <input
          className="min-w-0 flex-1 rounded-lg bg-slate-700 px-4 py-2 text-sm text-white placeholder-slate-400 outline-none focus:ring-2 focus:ring-violet-500"
          placeholder="Ask the AI advisor…"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          disabled={loading}
        />
        <button
          type="submit"
          disabled={loading}
          className="shrink-0 rounded-lg bg-violet-600 px-4 py-2 text-sm font-medium text-white hover:bg-violet-500 disabled:opacity-50 transition-colors"
        >
          Send
        </button>
      </form>
    </div>
  );
}
