using Avalonia.Controls;
using Avalonia.Threading;

namespace Svenska.App.Views;

public partial class ReferenceView : UserControl
{
    public ReferenceView() => InitializeComponent();

    /// <summary>Puts the cursor in the lookup box, ready to type a word.</summary>
    public void FocusLookup() => Dispatcher.UIThread.Post(() =>
    {
        LookupBox.Focus();
        LookupBox.SelectAll();
    });
}
