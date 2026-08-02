using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssetAutomator.WinUI.Controls;

/// <summary>
/// A thin Grid that exposes a resize-style cursor when hovered and fires
/// pointer events for the host to interpret as drag-resize gestures.
///
/// WinUI 3 only exposes <see cref="UIElement.ProtectedCursor"/> on UIElement
/// subclasses, so this tiny derived class is the only practical way to set
/// a custom system cursor on a drag-handle from MainWindow's code-behind.
/// <c>Border</c> is sealed, so we derive from <c>Grid</c> instead.
/// </summary>
public sealed class ResizableDragHandle : Grid
{
    public ResizableDragHandle()
    {
        // Set the resize cursor as soon as the control is created.
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    }

    /// <summary>Reset the cursor back to the default system arrow.</summary>
    public void ResetCursor() => ProtectedCursor = null;
}
