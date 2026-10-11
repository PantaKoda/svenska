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
/// Ctrl+Shift+Space = play slowly, Alt+A / Alt+E / Alt+O = å / ä / ö (with Shift: Å / Ä / Ö).
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
        else if ((modifiers & ~KeyModifiers.Shift) == KeyModifiers.Alt && e.Key is Key.A or Key.E or Key.O)
        {
            var letter = e.Key switch { Key.A => "å", Key.E => "ä", _ => "ö" };
            Insert(modifiers.HasFlag(KeyModifiers.Shift) ? letter.ToUpperInvariant() : letter);
            e.Handled = true;
        }
    }

    private void OnLetterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string letter })
            Insert(letter);
    }

    private void Insert(string letter)
    {
        if (AnswerBox.IsReadOnly)
            return;
        var text = AnswerBox.Text ?? "";
        var start = Math.Min(AnswerBox.SelectionStart, AnswerBox.SelectionEnd);
        var end = Math.Max(AnswerBox.SelectionStart, AnswerBox.SelectionEnd);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        AnswerBox.Text = text[..start] + letter + text[end..];
        AnswerBox.Focus();
        AnswerBox.CaretIndex = start + letter.Length;
        AnswerBox.SelectionStart = AnswerBox.SelectionEnd = start + letter.Length;
    }
}
