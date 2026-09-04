import { useCallback, useEffect, useRef, useState } from 'react';
import { VoiceSession, type VoiceStatus, type VoiceTranscript } from '../lib/VoiceSession';
import type { AdvisorArchitecture } from '../lib/responses-client';

interface VoiceButtonProps {
    onTranscript: (transcript: VoiceTranscript) => void;
    onConversationId?: (id: string) => void;
    onClearAudio?: () => void;
    disabled?: boolean;
    conversationId?: string;
    architecture: AdvisorArchitecture;
}

const STATUS_LABELS: Record<VoiceStatus, string> = {
    disconnected: '',
    connecting: 'Connecting...',
    ready: 'Voice active',
    listening: '🎤 Listening...',
    processing: '🤔 Processing...',
    function_calling: '🔧 Searching...',
};

export default function VoiceButton({
    onTranscript,
    onConversationId,
    onClearAudio,
    disabled,
    conversationId,
    architecture,
}: VoiceButtonProps) {
    const [status, setStatus] = useState<VoiceStatus>('disconnected');
    const [error, setError] = useState<string | null>(null);
    const sessionRef = useRef<VoiceSession | null>(null);

    const isActive = status !== 'disconnected';

    useEffect(() => () => {
        void sessionRef.current?.stop();
        sessionRef.current = null;
    }, []);

    const toggleVoice = useCallback(async () => {
        if (isActive) {
            await sessionRef.current?.stop();
            sessionRef.current = null;
            return;
        }

        setError(null);
        const session = new VoiceSession({
            onTranscript,
            onConversationId,
            onClearAudio,
            onStatus: (newStatus) => setStatus(newStatus),
            onError: (msg) => {
                setError(msg);
                console.error('Voice error:', msg);
            },
        }, conversationId, architecture);
        sessionRef.current = session;
        await session.start();
    }, [isActive, onTranscript, onConversationId, onClearAudio, conversationId, architecture]);

    const statusLabel = STATUS_LABELS[status];

    return (
        <div className="flex min-w-0 flex-wrap items-center gap-2">
            <button
                className={`shrink-0 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
                    isActive
                        ? 'bg-red-600 text-white hover:bg-red-500 animate-pulse'
                        : 'bg-slate-700 text-slate-300 hover:bg-slate-600 hover:text-white'
                } disabled:opacity-50`}
                onClick={toggleVoice}
                disabled={disabled}
                title={isActive ? 'Stop voice session' : 'Start voice session'}
            >
                {isActive ? '🔊 Stop' : '🎙️ Voice'}
            </button>
            {statusLabel && (
                <span className="min-w-0 break-words text-xs text-slate-400">{statusLabel}</span>
            )}
            {error && (
                <span className="text-xs text-red-400" title={error}>⚠️</span>
            )}
        </div>
    );
}
