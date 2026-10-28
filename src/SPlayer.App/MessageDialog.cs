using System.Runtime.InteropServices;

namespace SPlayer;

/// <summary>A native message box for failures that happen before XAML can show a dialog.</summary>
internal static class MessageDialog
{
    public static void Show(string message)
    {
        try { MessageBox(IntPtr.Zero, message, "SPlayer", 0x00000010); }
        catch (Exception) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
