using System.Windows;

namespace NothingShell.Notch.Dashboard;

/// <summary>What a tile may ask of the open notch (implemented by <see cref="NotchController"/>).</summary>
public interface IDashboardHost
{
    /// <summary>Replaces the grid with <paramref name="content"/> and holds the notch open ("expand") until closed.</summary>
    void ShowOverlay(FrameworkElement content, int rows = 2);

    void CloseOverlay();

    /// <summary>A text box wants the keyboard: activatable window + "text" hold.</summary>
    void BeginTyping();

    /// <summary>Releases the "text" hold (focus left the text box).</summary>
    void EndTyping();

    /// <summary>Closes the dashboard now.</summary>
    void Close();

    /// <summary>Switches to the page holding <paramref name="kind"/> (shelf on file drag).</summary>
    void ShowPageOf(Core.Notch.TileKind kind);
}
