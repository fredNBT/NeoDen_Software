using System.Windows.Input;

namespace NeoDenSoftware.ViewModels;

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);
}

public sealed class AsyncRelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await executeAsync(parameter);
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
