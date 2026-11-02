using AlpineAI.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace AlpineAI.App;

public sealed partial class MainPage
{
    private readonly VerticalStackLayout messages = new() { Spacing = 12, Padding = new Thickness(0, 8) };
    private readonly ScrollView messageScroll = new();
    private readonly Picker architecture = new()
    {
        Items = { "A2A agents", "MCP skills" },
        SelectedIndex = 0,
        TextColor = Colors.White,
        BackgroundColor = Control,
        FontSize = 12,
        AutomationId = "advisor-architecture"
    };
    private readonly Entry input = new()
    {
        Placeholder = "Ask the AI advisor...",
        TextColor = Colors.White,
        PlaceholderColor = Muted,
        BackgroundColor = Control,
        ReturnType = ReturnType.Send,
        AutomationId = "chat-input"
    };
    private readonly Button send = Button("Send", "send");
    private readonly Button newChat = Button("+ New Chat", "new-chat");
    private readonly Button voiceButton = Button("🎙️ Voice", "voice");
    private readonly Label voiceStatus = Text("", 12, Muted);
    private readonly Label voiceError = Text("", 12, Colors.Salmon);
    private readonly Dictionary<string, (Label Label, Border Bubble)> partialTranscripts = [];
    private VoiceSession? voice;
    private bool voiceActive;
    private bool voiceChanging;
    private bool busy;
    private string? conversationId;
    private AdvisorArchitecture Architecture => architecture.SelectedIndex == 1 ? AdvisorArchitecture.Skill : AdvisorArchitecture.A2A;

    private Border CreateChat()
    {
        voiceStatus.IsVisible = false;
        voiceError.IsVisible = false;
        var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var control in new View[] { architecture, voiceButton, newChat })
        {
            control.Margin = new Thickness(0, 0, 6, 6);
            actions.Children.Add(control);
        }
        var header = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(20, 20, 20, 0),
            Children = { Text("🤖 AI Advisor", 18, Violet, true), actions, voiceStatus, voiceError }
        };
        messageScroll.Content = messages;
        messageScroll.AutomationId = "chat-messages";
        messageScroll.Margin = new Thickness(20, 0);
        messageScroll.SizeChanged += (_, _) =>
        {
            if (messageScroll.Width <= 0) return;
            foreach (var bubble in messages.Children.OfType<Border>())
                bubble.MaximumWidthRequest = messageScroll.Width * .85;
        };
        messages.Add(Text("Ask about conditions, recommendations, or safety - by text or voice.", 14, Muted));
        var compose = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 8,
            Padding = new Thickness(20, 16)
        };
        send.BackgroundColor = Color.FromArgb("#7c3aed");
        compose.Add(input);
        compose.Add(send, 1);
        var layout = new Grid
        {
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            RowSpacing = 10
        };
        layout.Add(header);
        layout.Add(messageScroll, 0, 1);
        layout.Add(new VerticalStackLayout
        {
            Spacing = 0,
            Children = { new BoxView { Color = Control, HeightRequest = 1 }, compose }
        }, 0, 2);
        send.Clicked += async (_, _) => await SubmitAsync();
        input.Completed += async (_, _) => await SubmitAsync();
        newChat.Clicked += async (_, _) => await ResetConversationAsync();
        architecture.SelectedIndexChanged += async (_, _) => await ResetConversationAsync();
        voiceButton.Clicked += async (_, _) => await ToggleVoiceAsync();
        return Box(layout, Panel, 0, 16);
    }

    private async Task SubmitAsync()
    {
        if (busy || voiceActive || advisor is null || string.IsNullOrWhiteSpace(input.Text)) return;
        var text = input.Text.Trim();
        input.Text = "";
        chatting = SendAsync(text, true);
        await chatting;
    }

    private async Task ResetConversationAsync()
    {
        if (busy || voiceChanging || advisor is null) return;
        voiceChanging = true;
        UpdateChatControls();
        try
        {
            await StopVoiceAsync();
            conversationId = null;
            partialTranscripts.Clear();
            advisor.Reset();
            messages.Clear();
            chatting = SendAsync("Greet the user and briefly offer ski resort advisory help.", false);
            await chatting;
        }
        finally
        {
            voiceChanging = false;
            UpdateChatControls();
        }
    }

    private async Task SendAsync(string text, bool showUser)
    {
        if (advisor is null || connection is null) return;
        busy = true;
        UpdateChatControls();
        if (showUser) AddMessage(text, true);
        var (label, _) = AddMessage("Thinking...", false);
        var accumulated = "";
        var token = connection.Token;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            await foreach (var update in advisor.SendAsync(text, Architecture, conversationId, timeout.Token))
            {
                if (update.ConversationId is not null) conversationId = update.ConversationId;
                if (update.Content is null) continue;
                accumulated += update.Content;
                label.Text = accumulated;
                ScrollChatToEnd();
            }
            if (accumulated.Length == 0) label.Text = "(No response from advisor)";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { label.Text = "Request cancelled."; }
        catch (Exception ex) when (IsRequestError(ex))
        {
            logger.LogError(ex, "Advisor request failed");
            label.Text = $"{accumulated}\nError: {(ex is OperationCanceledException ? "Advisor request timed out." : ex.Message)}".TrimStart();
            label.TextColor = Colors.Salmon;
        }
        finally
        {
            busy = false;
            UpdateChatControls();
            ScrollChatToEnd();
        }
    }

    private (Label Label, Border Bubble) AddMessage(string text, bool user, bool isVoice = false)
    {
        var label = Text(text);
        var content = new VerticalStackLayout { Spacing = 4 };
        if (isVoice) content.Add(Text("VOICE", 10, Muted));
        content.Add(label);
        var bubble = Box(content, user ? Color.FromArgb("#7c3aed") : Control);
        bubble.Padding = new Thickness(16, 8);
        bubble.HorizontalOptions = user ? LayoutOptions.End : LayoutOptions.Start;
        bubble.MaximumWidthRequest = messageScroll.Width > 0 ? messageScroll.Width * .85 : 260;
        messages.Add(bubble);
        return (label, bubble);
    }

    private async Task ToggleVoiceAsync()
    {
        if (voiceChanging || busy || origin is null || connection is null) return;
        voiceChanging = true;
        UpdateChatControls();
        try
        {
            if (voiceActive) { await StopVoiceAsync(); return; }
            await StopVoiceAsync();
            voiceError.Text = "";
            voiceError.IsVisible = false;
            partialTranscripts.Clear();
            var session = new VoiceSession(new PortAudioDevice());
            voice = session;
            voiceActive = true;
            void Ui(Action update) => Dispatcher.Dispatch(() =>
            {
                if (ReferenceEquals(voice, session)) update();
            });
            session.Status += status => Ui(() =>
            {
                voiceStatus.Text = status;
                voiceStatus.IsVisible = status != "Disconnected";
                if (status == "Disconnected")
                {
                    voiceActive = false;
                    partialTranscripts.Clear();
                }
                UpdateChatControls();
            });
            session.Conversation += id => Ui(() => conversationId = id);
            session.Error += error => Ui(() =>
            {
                logger.LogError("Voice failed: {Error}", error);
                voiceError.Text = error;
                voiceError.IsVisible = true;
            });
            session.Transcript += transcript => Ui(() => ReceiveTranscript(transcript));
            session.ClearAudio += () => Ui(() =>
            {
                if (partialTranscripts.Remove("assistant", out var partial)) messages.Remove(partial.Bubble);
            });
            session.Start(origin, Architecture, conversationId);
        }
        finally
        {
            voiceChanging = false;
            UpdateChatControls();
        }
    }

    private void ReceiveTranscript(VoiceTranscript transcript)
    {
        if (partialTranscripts.TryGetValue(transcript.Role, out var current))
        {
            if (transcript.IsFinal)
            {
                if (!string.IsNullOrEmpty(transcript.Text)) current.Label.Text = transcript.Text;
                partialTranscripts.Remove(transcript.Role);
            }
            else current.Label.Text += transcript.Text;
        }
        else
        {
            current = AddMessage(transcript.Text, transcript.Role == "user", true);
            if (!transcript.IsFinal) partialTranscripts[transcript.Role] = current;
        }
        ScrollChatToEnd();
    }

    private void ScrollChatToEnd()
    {
        // Do not await MAUI's scroll task: the Avalonia handler can leave it pending during layout.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (messageScroll.Handler?.PlatformView is Avalonia.Controls.ScrollViewer scroll)
                scroll.ScrollToEnd();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private async Task StopVoiceAsync()
    {
        var previous = voice;
        voice = null;
        voiceActive = false;
        if (previous is not null) await previous.DisposeAsync();
        partialTranscripts.Clear();
        voiceStatus.Text = "";
        voiceStatus.IsVisible = false;
        UpdateChatControls();
    }

    private void UpdateChatControls()
    {
        var connected = connection is not null && !connection.IsCancellationRequested;
        input.IsEnabled = send.IsEnabled = connected && !busy && !voiceActive && !voiceChanging;
        architecture.IsEnabled = newChat.IsEnabled = connected && !busy && !voiceChanging;
        voiceButton.IsEnabled = connected && !busy && !voiceChanging;
        voiceButton.Text = voiceActive ? "🔊 Stop" : "🎙️ Voice";
        voiceButton.BackgroundColor = voiceActive ? Color.FromArgb("#dc2626") : Control;
    }
}
