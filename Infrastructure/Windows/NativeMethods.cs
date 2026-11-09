using System.Runtime.InteropServices;
using System.Text;

namespace McPetOverlay;

internal static class NativeMethods
{
	internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	internal delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential)]
	internal struct LowLevelMouseData
	{
		internal Point Point;
		internal uint MouseData;
		internal uint Flags;
		internal uint Time;
		internal nuint ExtraInfo;
	}

	internal struct Rect
	{
		internal int Left;

		internal int Top;

		internal int Right;

		internal int Bottom;
	}

	internal struct Point
	{
		internal int X;

		internal int Y;
	}

	internal struct Input
	{
		internal uint Type;

		internal InputUnion Data;
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct InputUnion
	{
		[FieldOffset(0)]
		internal MouseInput Mouse;

		[FieldOffset(0)]
		internal KeyboardInput Keyboard;
	}

	internal struct Size
	{
		internal int Width;
		internal int Height;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	internal struct BlendFunction
	{
		internal byte BlendOp;
		internal byte BlendFlags;
		internal byte SourceConstantAlpha;
		internal byte AlphaFormat;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct BitmapInfoHeader
	{
		internal uint Size;
		internal int Width;
		internal int Height;
		internal ushort Planes;
		internal ushort BitCount;
		internal uint Compression;
		internal uint SizeImage;
		internal int XPelsPerMeter;
		internal int YPelsPerMeter;
		internal uint ClrUsed;
		internal uint ClrImportant;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct BitmapInfo
	{
		internal BitmapInfoHeader Header;
	}

	internal enum AccentState
	{
		Disabled = 0,
		EnableTransparentGradient = 2,
		EnableBlurBehind = 3,
		EnableAcrylicBlurBehind = 4
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct AccentPolicy
	{
		internal AccentState AccentState;

		internal int AccentFlags;

		internal uint GradientColor;

		internal int AnimationId;
	}

	internal enum WindowCompositionAttribute
	{
		AccentPolicy = 19
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct WindowCompositionAttributeData
	{
		internal WindowCompositionAttribute Attribute;

		internal nint Data;

		internal int SizeOfData;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct Margins
	{
		internal int LeftWidth;

		internal int RightWidth;

		internal int TopHeight;

		internal int BottomHeight;
	}

	internal const int DwmwaUseImmersiveDarkMode = 20;

	internal const int DwmwaSystemBackdropType = 38;

	internal const int DwmsbtNone = 1;

	internal const int DwmsbtTransientWindow = 3;

#pragma warning disable CS0649 // Win32 INPUT fields intentionally default to zero when omitted.
	internal struct MouseInput
	{
		internal int Dx;

		internal int Dy;

		internal uint MouseData;

		internal uint Flags;

		internal uint Time;

		internal nuint ExtraInfo;
	}

	internal struct KeyboardInput
	{
		internal ushort VirtualKey;

		internal ushort ScanCode;

		internal uint Flags;

		internal uint Time;

		internal nuint ExtraInfo;
	}
#pragma warning restore CS0649

	internal const int GwlExStyle = -20;

	internal const int WsExTransparent = 32;

	internal const int WsExToolWindow = 128;

	internal const int WsExNoActivate = 134217728;

	internal const int SwHide = 0;

	internal const int SwShowNoActivate = 4;

	internal const int VkF8 = 119;

	internal const int VkF9 = 120;

	internal const int VkF10 = 121;

	internal const int VkRShift = 161;

	internal const int VkLShift = 160;

	internal const int VkShift = 16;

	internal const int VkLButton = 1;

	internal const int VkRButton = 2;
	internal const int VkMButton = 4;

	internal const int VkXButton1 = 5;

	internal const int VkXButton2 = 6;

	internal const int WhMouseLl = 14;

	internal const int HcAction = 0;

	internal const int WmRButtonDown = 0x0204;

	internal const int WmRButtonUp = 0x0205;

	internal const int WmMButtonDown = 0x0207;

	internal const int WmXButtonDown = 0x020B;

	internal const uint LlMouseInjected = 1u;

	internal const uint LlMouseLowerIlInjected = 2u;

	internal const int VkW = 87;

	internal const int VkA = 65;

	internal const int VkS = 83;

	internal const int VkD = 68;

	internal const int VkC = 67;

	internal const uint InputMouse = 0u;

	internal const uint InputKeyboard = 1u;

	internal const uint MouseEventLeftDown = 2u;

	internal const uint MouseEventLeftUp = 4u;

	internal const uint MouseEventRightDown = 8u;

	internal const uint MouseEventRightUp = 16u;

	internal const uint MouseEventMove = 1u;

	internal const uint KeyEventKeyUp = 2u;

	internal const int WmNcHitTest = 132;

	internal const int HtTransparent = -1;

	internal const int WsExLayered = 524288;

	internal const uint SwpNoSize = 1u;

	internal const uint SwpNoMove = 2u;

	internal const uint SwpNoZOrder = 4u;

	internal const uint SwpFrameChanged = 32u;
	internal const uint SwpNoActivate = 16u;
	internal const int HwndTopmost = -1;
	internal const uint UlwAlpha = 2u;
	internal const byte AcSrcOver = 0;
	internal const byte AcSrcAlpha = 1;

	internal const int VkControl = 17;

	internal const int VkLControl = 162;

	internal const int VkRControl = 163;

	internal const uint ProcessVmRead = 16u;

	internal const uint ProcessQueryLimitedInformation = 4096u;

	[DllImport("user32.dll")]
	internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	internal static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	internal static extern int GetClassName(nint hWnd, StringBuilder text, int maxCount);

	[DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	internal static extern uint TimeBeginPeriod(uint milliseconds);

	[DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
	internal static extern uint TimeEndPeriod(uint milliseconds);

	[DllImport("user32.dll")]
	internal static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	internal static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll")]
	internal static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	[DllImport("user32.dll")]
	internal static extern bool GetClientRect(nint hWnd, out Rect rect);

	[DllImport("user32.dll")]
	internal static extern bool ClientToScreen(nint hWnd, ref Point point);

	[DllImport("user32.dll")]
	internal static extern nint GetWindowLongPtr(nint hWnd, int index);

	[DllImport("user32.dll")]
	internal static extern nint SetWindowLongPtr(nint hWnd, int index, nint newStyle);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int SetWindowLong(nint hWnd, int index, int newStyle);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool UpdateLayeredWindow(nint hWnd, nint hdcDst, ref Point ptDst, ref Size size, nint hdcSrc, ref Point ptSrc, uint crKey, ref BlendFunction blend, uint flags);

	[DllImport("user32.dll")]
	internal static extern nint GetDC(nint hWnd);

	[DllImport("user32.dll")]
	internal static extern int ReleaseDC(nint hWnd, nint hDC);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateCompatibleDC(nint hdc);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern bool DeleteDC(nint hdc);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint SelectObject(nint hdc, nint obj);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern bool DeleteObject(nint obj);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateDIBSection(nint hdc, ref BitmapInfo bitmapInfo, uint usage, out nint bits, nint section, uint offset);

	[DllImport("user32.dll")]
	internal static extern int SetWindowCompositionAttribute(nint hWnd, ref WindowCompositionAttributeData data);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmSetWindowAttribute(nint hWnd, int attribute, ref int value, int valueSize);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmExtendFrameIntoClientArea(nint hWnd, ref Margins margins);

	[DllImport("user32.dll")]
	internal static extern bool ShowWindow(nint hWnd, int command);

	[DllImport("user32.dll")]
	internal static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	internal static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll")]
	internal static extern short GetAsyncKeyState(int virtualKey);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SetWindowsHookExW(int idHook, LowLevelMouseProc callback, nint module, uint threadId);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool UnhookWindowsHookEx(nint hook);

	[DllImport("user32.dll")]
	internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint GetModuleHandleW(string? moduleName);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint SendInput(uint count, Input[] inputs, int size);

	[DllImport("user32.dll")]
	internal static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll")]
	internal static extern bool GetCursorPos(out Point point);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool ReadProcessMemory(nint process, nint baseAddress, [Out] byte[] buffer, int size, out nint bytesRead);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool CloseHandle(nint handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool AllocConsole();
}
