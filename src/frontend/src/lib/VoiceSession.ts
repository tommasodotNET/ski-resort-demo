import type { AdvisorArchitecture } from './responses-client';

export interface VoiceTranscript {
    role: 'user' | 'assistant';
    text: string;
    isFinal: boolean;
}

export type VoiceStatus = 'disconnected' | 'connecting' | 'ready' | 'listening' | 'processing' | 'function_calling';

export interface VoiceSessionCallbacks {
    onTranscript: (transcript: VoiceTranscript) => void;
    onStatus: (status: VoiceStatus, detail?: string) => void;
    onError: (message: string) => void;
    onConversationId?: (id: string) => void;
    /** Called when the server clears the audio queue (e.g. agent interrupted) */
    onClearAudio?: () => void;
}

const PCM_SAMPLE_RATE = 24000;
const PCM_CHUNK_MS = 50;
const PCM_CHUNK_SAMPLES = (PCM_SAMPLE_RATE * PCM_CHUNK_MS) / 1000; // 1200 samples per chunk

// AudioWorklet processor code (inline to avoid separate file)
const WORKLET_CODE = `
class PCMCaptureProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.buffer = new Float32Array(0);
    }

    process(inputs) {
        const input = inputs[0];
        if (!input || !input[0]) return true;
        const channelData = input[0];

        // Accumulate samples
        const newBuffer = new Float32Array(this.buffer.length + channelData.length);
        newBuffer.set(this.buffer);
        newBuffer.set(channelData, this.buffer.length);
        this.buffer = newBuffer;

        // Send chunks of ${PCM_CHUNK_SAMPLES} samples
        while (this.buffer.length >= ${PCM_CHUNK_SAMPLES}) {
            const chunk = this.buffer.slice(0, ${PCM_CHUNK_SAMPLES});
            this.buffer = this.buffer.slice(${PCM_CHUNK_SAMPLES});

            // Convert Float32 to Int16
            const int16 = new Int16Array(chunk.length);
            for (let i = 0; i < chunk.length; i++) {
                const s = Math.max(-1, Math.min(1, chunk[i]));
                int16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
            }
            this.port.postMessage(int16.buffer, [int16.buffer]);
        }
        return true;
    }
}
registerProcessor('pcm-capture-processor', PCMCaptureProcessor);
`;

export class VoiceSession {
    private ws: WebSocket | null = null;
    private audioContext: AudioContext | null = null;
    private mediaStream: MediaStream | null = null;
    private workletNode: AudioWorkletNode | null = null;
    private sourceNode: MediaStreamAudioSourceNode | null = null;
    private callbacks: VoiceSessionCallbacks;
    private _status: VoiceStatus = 'disconnected';
    private readyTimeout: number | null = null;

    // Playback
    private playbackContext: AudioContext | null = null;
    private nextPlaybackTime = 0;
    private scheduledSources: AudioBufferSourceNode[] = [];

    private conversationId?: string;
    private architecture: AdvisorArchitecture;

    constructor(
        callbacks: VoiceSessionCallbacks,
        conversationId?: string,
        architecture: AdvisorArchitecture = 'a2a',
    ) {
        this.callbacks = callbacks;
        this.conversationId = conversationId;
        this.architecture = architecture;
    }

    get status(): VoiceStatus {
        return this._status;
    }

    get isActive(): boolean {
        return this._status !== 'disconnected';
    }

    async start(): Promise<void> {
        if (this.isActive) return;
        this.setStatus('connecting');

        try {
            const wsProtocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            let wsUrl = `${wsProtocol}//${window.location.host}/ws/voice/${this.architecture}`;
            if (this.conversationId) {
                wsUrl += `?conversationId=${encodeURIComponent(this.conversationId)}`;
            }
            this.ws = new WebSocket(wsUrl);

            await new Promise<void>((resolve, reject) => {
                const openTimeout = window.setTimeout(
                    () => reject(new Error('WebSocket connection timeout')),
                    10000,
                );
                this.ws!.onopen = () => {
                    window.clearTimeout(openTimeout);
                    resolve();
                };
                this.ws!.onerror = () => {
                    window.clearTimeout(openTimeout);
                    reject(new Error('WebSocket connection failed'));
                };
            });

            this.ws.onmessage = (event) => this.handleServerMessage(event.data);
            this.ws.onclose = () => this.handleDisconnect();
            this.ws.onerror = () => this.callbacks.onError('WebSocket error');
            this.readyTimeout = window.setTimeout(() => {
                if (this._status === 'connecting') {
                    this.callbacks.onError('Voice service did not become ready in time');
                    void this.stop();
                }
            }, 35000);

            await this.startAudioCapture();

            this.playbackContext = new AudioContext({ sampleRate: PCM_SAMPLE_RATE });
            this.nextPlaybackTime = 0;

        } catch (error) {
            this.callbacks.onError(error instanceof Error ? error.message : String(error));
            await this.stop();
        }
    }

    async stop(): Promise<void> {
        if (this.readyTimeout !== null) {
            window.clearTimeout(this.readyTimeout);
            this.readyTimeout = null;
        }

        if (this.ws?.readyState === WebSocket.OPEN) {
            this.ws.send(JSON.stringify({ type: 'stop' }));
            this.ws.close();
        }
        this.ws = null;

        this.workletNode?.disconnect();
        this.workletNode = null;
        this.sourceNode?.disconnect();
        this.sourceNode = null;

        if (this.mediaStream) {
            this.mediaStream.getTracks().forEach(t => t.stop());
            this.mediaStream = null;
        }

        await this.audioContext?.close();
        this.audioContext = null;

        await this.playbackContext?.close();
        this.playbackContext = null;
        this.scheduledSources = [];
        this.nextPlaybackTime = 0;

        this.setStatus('disconnected');
    }

    private async startAudioCapture(): Promise<void> {
        this.mediaStream = await navigator.mediaDevices.getUserMedia({
            audio: {
                echoCancellation: true,
                noiseSuppression: true,
                sampleRate: PCM_SAMPLE_RATE,
            }
        });

        this.audioContext = new AudioContext({ sampleRate: PCM_SAMPLE_RATE });

        const blob = new Blob([WORKLET_CODE], { type: 'application/javascript' });
        const workletUrl = URL.createObjectURL(blob);
        await this.audioContext.audioWorklet.addModule(workletUrl);
        URL.revokeObjectURL(workletUrl);

        this.sourceNode = this.audioContext.createMediaStreamSource(this.mediaStream);
        this.workletNode = new AudioWorkletNode(this.audioContext, 'pcm-capture-processor');

        this.workletNode.port.onmessage = (event: MessageEvent) => {
            if (this.ws?.readyState === WebSocket.OPEN) {
                const int16Buffer = event.data as ArrayBuffer;
                const base64 = arrayBufferToBase64(int16Buffer);
                this.ws.send(JSON.stringify({ type: 'audio', data: base64 }));
            }
        };

        this.sourceNode.connect(this.workletNode);
        this.workletNode.connect(this.audioContext.destination);
    }

    private handleServerMessage(data: string): void {
        try {
            const msg = JSON.parse(data);

            switch (msg.type) {
                case 'ready':
                    if (this.readyTimeout !== null) {
                        window.clearTimeout(this.readyTimeout);
                        this.readyTimeout = null;
                    }
                    this.setStatus('ready');
                    if (msg.conversationId) {
                        this.callbacks.onConversationId?.(msg.conversationId);
                    }
                    break;

                case 'audio':
                    this.playAudio(msg.data);
                    break;

                case 'clear_audio':
                    this.clearPlaybackQueue();
                    this.callbacks.onClearAudio?.();
                    break;

                case 'transcript':
                    this.callbacks.onTranscript({
                        role: msg.role,
                        text: msg.text,
                        isFinal: msg.final_ ?? msg.final ?? false,
                    });
                    break;

                case 'status':
                    this.setStatus(msg.status as VoiceStatus);
                    break;

                case 'error':
                    this.callbacks.onError(msg.message);
                    void this.stop();
                    break;
            }
        } catch (error) {
            console.error('Error parsing server message:', error);
        }
    }

    private playAudio(base64Data: string): void {
        if (!this.playbackContext) return;

        const bytes = base64ToArrayBuffer(base64Data);
        const int16 = new Int16Array(bytes);

        const float32 = new Float32Array(int16.length);
        for (let i = 0; i < int16.length; i++) {
            float32[i] = int16[i] / 32768.0;
        }

        const audioBuffer = this.playbackContext.createBuffer(1, float32.length, PCM_SAMPLE_RATE);
        audioBuffer.getChannelData(0).set(float32);

        const source = this.playbackContext.createBufferSource();
        source.buffer = audioBuffer;
        source.connect(this.playbackContext.destination);
        source.onended = () => {
            const idx = this.scheduledSources.indexOf(source);
            if (idx !== -1) this.scheduledSources.splice(idx, 1);
        };
        this.scheduledSources.push(source);

        const currentTime = this.playbackContext.currentTime;
        const startTime = Math.max(currentTime, this.nextPlaybackTime);
        source.start(startTime);
        this.nextPlaybackTime = startTime + audioBuffer.duration;
    }

    private clearPlaybackQueue(): void {
        for (const source of this.scheduledSources) {
            try { source.stop(); } catch { /* already stopped */ }
        }
        this.scheduledSources = [];
        this.nextPlaybackTime = 0;
    }

    private handleDisconnect(): void {
        if (this._status !== 'disconnected') {
            this.stop();
        }
    }

    private setStatus(status: VoiceStatus): void {
        this._status = status;
        this.callbacks.onStatus(status);
    }
}

function arrayBufferToBase64(buffer: ArrayBuffer): string {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.length; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary);
}

function base64ToArrayBuffer(base64: string): ArrayBuffer {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
}
