using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Svenska.App.ViewModels;

namespace Svenska.App.Views;

/// <summary>
/// Keys that work everywhere: F1 opens and closes the reference panel, Esc closes it,
/// Alt+A / Alt+E / Alt+O type å / ä / ö into the text box in use (with Shift: Å / Ä / Ö).
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var modifiers = e.KeyModifiers;
        if (e.Key == Key.F1 && modifiers == KeyModifiers.None)
        {
            vm.Reference.IsOpen = !vm.Reference.IsOpen;
            if (vm.Reference.IsOpen)
            {
                vm.Reference.SelectedTab = 0;
                ReferencePanel.FocusLookup();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.Reference.IsOpen)
        {
            vm.Reference.IsOpen = false;
            e.Handled = true;
        }
        else if ((modifiers & ~KeyModifiers.Shift) == KeyModifiers.Alt && e.Key is Key.A or Key.E or Key.O
                 && FocusManager?.GetFocusedElement() is TextBox box)
        {
            var letter = e.Key switch { Key.A => "å", Key.E => "ä", _ => "ö" };
            SwedishLetters.Insert(box, modifiers.HasFlag(KeyModifiers.Shift) ? letter.ToUpperInvariant() : letter);
            e.Handled = true;
        }
    }
}

public static class SwedishLetters
{
    /// <summary>Types a letter at the cursor of a text box, replacing the selection.</summary>
    public static void Insert(TextBox box, string letter)
    {
        if (box.IsReadOnly)
            return;
        var text = box.Text ?? "";
        var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), start, text.Length);
        box.Text = text[..start] + letter + text[end..];
        box.Focus();
        box.CaretIndex = start + letter.Length;
        box.SelectionStart = box.SelectionEnd = start + letter.Length;
    }
}
