using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Svenska.App.ViewModels;

namespace Svenska.App.Views;

/// <summary>
/// Keyboard for the exercise player: Enter = check / next, Ctrl+Space = play again,
/// Ctrl+Shift+Space = play slowly. (å ä ö shortcuts are handled by the window.)
/// </summary>
public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PlayerViewModel vm)
                vm.PropertyChanged += OnViewModelChanged;
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FocusAnswer();
    }

    // A new item or a clicked word: put the cursor back at the end of the answer.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.Position) or nameof(PlayerViewModel.Answer))
            FocusAnswer();
    }

    private void FocusAnswer() => Dispatcher.UIThread.Post(() =>
    {
        AnswerBox.Focus();
        if (AnswerBox.SelectionStart == AnswerBox.SelectionEnd)
            AnswerBox.CaretIndex = AnswerBox.Text?.Length ?? 0;
    });

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        var modifiers = e.KeyModifiers;
        if (e.Key == Key.Enter && modifiers == KeyModifiers.None)
        {
            vm.SubmitCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && modifiers == KeyModifiers.Control)
        {
            vm.PlayCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && modifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            vm.PlaySlowCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnLetterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string letter })
            SwedishLetters.Insert(AnswerBox, letter);
    }
}
