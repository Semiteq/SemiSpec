using System.Runtime.InteropServices;

namespace SemiSpec.UI.Instance;

/// <summary>The user32 calls the single-instance hand-over needs and Avalonia does not wrap.</summary>
internal static class NativeMethods
{
	public const uint OkButtonWithWarningIcon = 0x00000030;
	public const int AnyProcess = -1;
	public const int RestoreCommand = 9;

	[DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	internal static extern int MessageBoxW(nint ownerWindow, string text, string caption, uint type);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool AllowSetForegroundWindow(int processId);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool ShowWindow(nint window, int command);
}
