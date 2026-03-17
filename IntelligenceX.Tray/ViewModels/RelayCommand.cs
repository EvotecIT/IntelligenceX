using System.Windows.Input;

namespace IntelligenceX.Tray.ViewModels;

/// <summary>
/// Simple relay command for MVVM bindings.
/// </summary>
public sealed class RelayCommand : ICommand {
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null) {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter) {
        if (_isExecuting) {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try {
            await _execute();
        } finally {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
