using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Svenska.App.ViewModels;

namespace Svenska.App.Views;

/// <summary>
/// Keyboard and mouse for the exercise player: Enter = check / next, Ctrl+Space = play
/// again, Ctrl+Shift+Space = play slowly, 1–9 = pick a choice, a click in the scene.
/// (å ä ö shortcuts are handled by the window.)
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
        if (DataContext is PlayerViewModel { IsTyped: false })
        {
            Focus(); // no text box: the keys (1–9, Enter) go to the player itself
            return;
        }
        AnswerBox.Focus();
        if (AnswerBox.SelectionStart == AnswerBox.SelectionEnd)
            AnswerBox.CaretIndex = AnswerBox.Text?.Length ?? 0;
    });

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        var modifiers = e.KeyModifiers;
        // In free writing Enter is a new line; Ctrl+Enter finishes.
        var writing = vm is { IsFreeWrite: true, IsChecked: false };
        if (e.Key == Key.Enter && (writing ? modifiers == KeyModifiers.Control : modifiers == KeyModifiers.None))
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
        else if (modifiers == KeyModifiers.None && vm is { IsTyped: false, Options.Count: > 0 }
                 && e.Key is >= Key.D1 and <= Key.D9 or >= Key.NumPad1 and <= Key.NumPad9)
        {
            vm.ChooseOption(e.Key >= Key.NumPad1 ? e.Key - Key.NumPad1 + 1 : e.Key - Key.D1 + 1);
            e.Handled = true;
        }
    }

    /// <summary>A click in the scene: from the place in the control to the place in the drawing.</summary>
    private void OnScenePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;
        var (width, height) = vm.SceneSize;
        var box = SceneImage.Bounds;
        if (width <= 0 || box.Width <= 0)
            return;
        // Stretch="Uniform": the drawing is scaled to fit and centred in the control.
        var scale = Math.Min(box.Width / width, box.Height / height);
        var point = e.GetPosition(SceneImage);
        vm.SceneClick((point.X - (box.Width - width * scale) / 2) / scale, (point.Y - (box.Height - height * scale) / 2) / scale);
    }

    private void OnLetterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string letter })
            SwedishLetters.Insert(AnswerBox, letter);
    }
}
