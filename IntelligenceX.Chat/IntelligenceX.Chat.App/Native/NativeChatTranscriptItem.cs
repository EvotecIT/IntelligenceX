using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IntelligenceX.Chat.App.Native.Rendering;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native transcript item consumed by WinUI controls without rendered HTML.
/// </summary>
internal sealed class NativeChatTranscriptItem : INotifyPropertyChanged {
    private IReadOnlyList<NativeTranscriptContent> _content;
    private string _text;
    private string _status;

    public NativeChatTranscriptItem(
        string role,
        string text,
        DateTimeOffset createdAt,
        string status = "",
        string? model = null) {
        Role = string.IsNullOrWhiteSpace(role) ? "system" : role.Trim();
        _text = text ?? string.Empty;
        CreatedAt = createdAt;
        _status = status ?? string.Empty;
        Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        _content = ShouldProjectContent() ? NativeMarkdownProjection.Project(Role, _text) : Array.Empty<NativeTranscriptContent>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Role { get; }

    public DateTimeOffset CreatedAt { get; }

    public string? Model { get; private set; }

    public IReadOnlyList<NativeTranscriptContent> Content => _content;

    public string Text {
        get => _text;
        set {
            if (string.Equals(_text, value, StringComparison.Ordinal)) {
                return;
            }

            _text = value ?? string.Empty;
            RefreshContentIfReady();
            OnPropertyChanged();
        }
    }

    public string Status {
        get => _status;
        set {
            if (string.Equals(_status, value, StringComparison.Ordinal)) {
                return;
            }

            _status = value ?? string.Empty;
            RefreshContentIfReady();
            OnPropertyChanged();
        }
    }

    public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase);

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

    public void AppendText(string value) {
        if (string.IsNullOrEmpty(value)) {
            return;
        }

        Text += value;
    }

    /// <summary>
    /// Records the resolved model used for an assistant response before it is persisted.
    /// </summary>
    public void SetModel(string? model) {
        var normalized = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (string.Equals(Model, normalized, StringComparison.Ordinal)) {
            return;
        }

        Model = normalized;
        OnPropertyChanged(nameof(Model));
    }

    private void RefreshContentIfReady() {
        if (!ShouldProjectContent()) {
            _content = Array.Empty<NativeTranscriptContent>();
            OnPropertyChanged(nameof(Content));
            return;
        }

        _content = NativeMarkdownProjection.Project(Role, _text);
        OnPropertyChanged(nameof(Content));
    }

    private bool ShouldProjectContent() =>
        !IsAssistant
        || string.Equals(_status, "Complete", StringComparison.OrdinalIgnoreCase)
        || string.Equals(_status, "Error", StringComparison.OrdinalIgnoreCase)
        || string.Equals(_status, "Canceled", StringComparison.OrdinalIgnoreCase);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
