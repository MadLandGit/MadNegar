using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MadNegar;

internal sealed record GlobalHotkeyPreset(string DisplayName, uint Modifiers, Keys Key);
internal sealed record DirectTypingTarget(
    IntPtr RootWindow,
    IntPtr FocusWindow,
    uint ProcessId,
    string RootClassName,
    string FocusClassName,
    bool HasCaret);
internal sealed record DirectTypingResult(bool Success, string Message, bool RetrySafe = true);

/// <summary>Windows global-shortcut registration and compatibility-oriented direct text insertion.</summary>
internal static class GlobalHotkeySupport
{
    public const int MessageId = 0x4D4E;
    public const int WmHotkey = 0x0312;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint InputKeyboard = 1;
    private const uint GaRoot = 2;
    private const uint GuiCaretBlinking = 0x00000001;
    private const int VkControl = 0x11;
    private const int VkV = 0x56;

    public static string DefaultShortcut => "Ctrl+Alt+Space";

    public static string NormalizeShortcut(string? shortcut) =>
        TryParseShortcut(shortcut, out var parsed) ? parsed.DisplayName : DefaultShortcut;

    public static bool TryParseShortcut(string? shortcut, out GlobalHotkeyPreset parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        uint modifiers = ModNoRepeat;
        var sawModifier = false;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    if ((modifiers & ModControl) != 0) return false;
                    modifiers |= ModControl;
                    sawModifier = true;
                    break;
                case "alt":
                    if ((modifiers & ModAlt) != 0) return false;
                    modifiers |= ModAlt;
                    sawModifier = true;
                    break;
                case "shift":
                    if ((modifiers & ModShift) != 0) return false;
                    modifiers |= ModShift;
                    sawModifier = true;
                    break;
                case "win":
                case "windows":
                    if ((modifiers & ModWin) != 0) return false;
                    modifiers |= ModWin;
                    sawModifier = true;
                    break;
                default:
                    return false;
            }
        }

        if (!sawModifier || !TryParseKey(parts[^1], out var key) || IsModifierKey(key) || key == Keys.F12)
            return false;

        var displayParts = new List<string>();
        if ((modifiers & ModControl) != 0) displayParts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) displayParts.Add("Alt");
        if ((modifiers & ModShift) != 0) displayParts.Add("Shift");
        if ((modifiers & ModWin) != 0) displayParts.Add("Win");
        displayParts.Add(FormatKey(key));
        parsed = new GlobalHotkeyPreset(string.Join("+", displayParts), modifiers, key);
        return true;
    }

    public static bool TryCreateShortcut(Keys key, bool control, bool alt, bool shift, bool windows, out string shortcut, out string error)
    {
        shortcut = string.Empty;
        error = string.Empty;
        key &= Keys.KeyCode;

        uint modifiers = ModNoRepeat;
        if (control) modifiers |= ModControl;
        if (alt) modifiers |= ModAlt;
        if (shift) modifiers |= ModShift;
        if (windows) modifiers |= ModWin;

        if ((modifiers & (ModControl | ModAlt | ModShift | ModWin)) == 0)
        {
            error = "Use at least one modifier (Ctrl, Alt, Shift or Win) plus a key.";
            return false;
        }
        if (key == Keys.None || IsModifierKey(key))
        {
            error = "Press a non-modifier key together with the modifier keys.";
            return false;
        }
        if (key == Keys.F12)
        {
            error = "F12 is reserved by Windows for debugging and cannot be used reliably.";
            return false;
        }

        var parts = new List<string>();
        if (control) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (windows) parts.Add("Win");
        parts.Add(FormatKey(key));
        shortcut = string.Join("+", parts);
        return true;
    }

    public static bool IsWindowsKeyDown() =>
        GetAsyncKeyState((int)Keys.LWin) < 0 || GetAsyncKeyState((int)Keys.RWin) < 0;

    public static bool TryRegister(IntPtr windowHandle, string shortcut, out int errorCode)
    {
        errorCode = 0;
        if (!TryParseShortcut(shortcut, out var parsed))
        {
            errorCode = 87; // ERROR_INVALID_PARAMETER
            return false;
        }

        var success = RegisterHotKey(windowHandle, MessageId, parsed.Modifiers, (uint)parsed.Key);
        if (!success) errorCode = Marshal.GetLastWin32Error();
        return success;
    }

    public static void Unregister(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
            _ = UnregisterHotKey(windowHandle, MessageId);
    }

    public static bool TryCaptureForegroundTarget(IntPtr madNegarWindow, out DirectTypingTarget? target, out string reason)
    {
        target = null;
        reason = string.Empty;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsWindow(foreground))
        {
            reason = "Windows did not report an active destination window.";
            return false;
        }

        var root = GetAncestor(foreground, GaRoot);
        if (root == IntPtr.Zero) root = foreground;
        _ = GetWindowThreadProcessId(root, out var processId);
        _ = GetWindowThreadProcessId(madNegarWindow, out var ownProcessId);
        if (processId == 0 || processId == ownProcessId)
        {
            reason = "Select a text field in another application before starting voice typing.";
            return false;
        }

        var threadId = GetWindowThreadProcessId(foreground, out _);
        var guiInfo = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        var hasGuiInfo = threadId != 0 && GetGUIThreadInfo(threadId, ref guiInfo);
        var focusWindow = hasGuiInfo && guiInfo.FocusWindow != IntPtr.Zero ? guiInfo.FocusWindow : foreground;
        if (focusWindow == IntPtr.Zero || !IsWindow(focusWindow))
        {
            reason = "Windows could not identify the focused control. Click inside the destination field and try again.";
            return false;
        }

        var rootClass = GetWindowClassName(root);
        var focusClass = GetWindowClassName(focusWindow);
        var hasCaret = hasGuiInfo && (guiInfo.Flags & GuiCaretBlinking) != 0;
        if (IsExplorerWindowClass(rootClass) && !IsEditableControlClass(focusClass) && !hasCaret)
        {
            reason = "In File Explorer, first activate a text field such as the filename box after pressing F2. MadNegar will not paste into the file list.";
            return false;
        }
        if (!hasCaret && !IsEditableControlClass(focusClass) && IsClearlyNonTextControlClass(focusClass))
        {
            reason = "The focused control looks like a button, menu, or list rather than a text field. Click inside a text field and try again.";
            return false;
        }

        target = new DirectTypingTarget(root, focusWindow, processId, rootClass, focusClass, hasCaret);
        return true;
    }

    public static bool IsCapturedTargetStillFocused(DirectTypingTarget target)
    {
        if (!IsWindow(target.RootWindow) || !IsWindow(target.FocusWindow)) return false;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsWindow(foreground)) return false;
        var root = GetAncestor(foreground, GaRoot);
        if (root == IntPtr.Zero) root = foreground;
        if (root != target.RootWindow) return false;
        _ = GetWindowThreadProcessId(root, out var processId);
        if (processId != target.ProcessId) return false;

        var threadId = GetWindowThreadProcessId(foreground, out _);
        return GetFocusedWindow(threadId, foreground) == target.FocusWindow;
    }

    /// <summary>Reactivates the captured application for an explicit user-requested retry.</summary>
    public static async Task<bool> TryReactivateCapturedTargetAsync(DirectTypingTarget target, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || !IsWindow(target.RootWindow) || !IsWindow(target.FocusWindow)) return false;
        if (IsCapturedTargetStillFocused(target)) return true;
        if (!SetForegroundWindow(target.RootWindow)) return false;
        await Task.Delay(180, cancellationToken);
        return !cancellationToken.IsCancellationRequested && IsCapturedTargetStillFocused(target);
    }

    /// <summary>
    /// Pastes the exact finalized transcript into the original focused control. The prior clipboard data is
    /// restored after the target has had time to process Ctrl+V, unless another app/user changed the clipboard.
    /// Explorer's file list is explicitly rejected so Ctrl+V cannot paste copied files by accident.
    /// </summary>
    public static async Task<DirectTypingResult> TryPasteTextPreservingClipboardAsync(
        string text, DirectTypingTarget target, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new DirectTypingResult(false, "The transcription is empty; nothing was inserted.");
        if (cancellationToken.IsCancellationRequested)
            return new DirectTypingResult(false, "Voice typing was cancelled before insertion.");
        if (!IsCapturedTargetStillFocused(target))
            return new DirectTypingResult(false, "The destination window or focused field changed during transcription. The text was kept in MadNegar and was not sent.");
        if (IsExplorerWindowClass(target.RootClassName) && !IsEditableControlClass(target.FocusClassName) && !target.HasCaret)
            return new DirectTypingResult(false, "The focused Explorer control is not a filename text field. Nothing was pasted; the text remains in MadNegar.");
        if (!target.HasCaret && !IsEditableControlClass(target.FocusClassName) && IsClearlyNonTextControlClass(target.FocusClassName))
            return new DirectTypingResult(false, "The destination is a non-text control. Nothing was pasted; click a text field or use Copy Text.");

        IDataObject? previousClipboard;
        uint originalClipboardSequence;
        try
        {
            previousClipboard = Clipboard.GetDataObject();
            originalClipboardSequence = GetClipboardSequenceNumber();
        }
        catch (Exception ex)
        {
            return new DirectTypingResult(false, $"MadNegar could not safely read the current clipboard ({ex.Message}). Nothing was inserted; use Copy Text to paste manually.");
        }

        var clipboardWasSet = false;
        var inputDispatched = false;
        uint injectedClipboardSequence = 0;
        try
        {
            Exception? clipboardError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    var restoredOnCancel = clipboardWasSet && TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
                    return new DirectTypingResult(false, "Voice typing was cancelled before insertion." + (restoredOnCancel ? " The previous clipboard was restored." : ""));
                }
                var currentClipboardSequence = GetClipboardSequenceNumber();
                if (currentClipboardSequence != originalClipboardSequence &&
                    (injectedClipboardSequence == 0 || currentClipboardSequence != injectedClipboardSequence))
                    return new DirectTypingResult(false, "The clipboard changed while MadNegar was preparing text. The newer clipboard contents were left untouched.");
                try
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    clipboardWasSet = true;
                    injectedClipboardSequence = GetClipboardSequenceNumber();
                    if (Clipboard.ContainsText(TextDataFormat.UnicodeText)
                        && string.Equals(Clipboard.GetText(TextDataFormat.UnicodeText), text, StringComparison.Ordinal))
                    {
                        clipboardError = null;
                        break;
                    }
                    clipboardError = new InvalidOperationException("Windows did not retain the exact Unicode text on the clipboard.");
                    if (GetClipboardSequenceNumber() != injectedClipboardSequence)
                        return new DirectTypingResult(false, "The clipboard changed while MadNegar prepared the transcript. Nothing was pasted and the newer clipboard contents were left untouched.");
                }
                catch (Exception ex)
                {
                    clipboardError = ex;
                    if (GetClipboardSequenceNumber() != originalClipboardSequence &&
                        (injectedClipboardSequence == 0 || GetClipboardSequenceNumber() != injectedClipboardSequence))
                        return new DirectTypingResult(false, "The clipboard changed while MadNegar was preparing text. The newer clipboard contents were left untouched.");
                }
                if (attempt < 2) await Task.Delay(90);
            }
            if (clipboardError is not null || injectedClipboardSequence == 0)
            {
                var restored = clipboardWasSet && TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
                return new DirectTypingResult(false,
                    $"Windows could not prepare the transcript on the clipboard after three attempts ({clipboardError?.Message ?? "unknown clipboard error"}). Nothing was inserted; use Copy Text." +
                    (restored ? " The previous clipboard was restored." : ""));
            }

            // Let the clipboard owner settle, then verify the same destination still has focus immediately before Ctrl+V.
            await Task.Delay(50);
            if (cancellationToken.IsCancellationRequested)
            {
                var restoredOnCancel = TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
                return new DirectTypingResult(false, "Voice typing was cancelled before the paste was sent." + (restoredOnCancel ? " The previous clipboard was restored." : ""));
            }
            if (!IsCapturedTargetStillFocused(target))
            {
                var restored = TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
                return new DirectTypingResult(false, restored
                    ? "The destination field lost focus before insertion. Nothing was pasted and the previous clipboard was restored."
                    : "The destination field lost focus before insertion. Nothing was pasted; the clipboard changed meanwhile and was left untouched.");
            }
            if (GetClipboardSequenceNumber() != injectedClipboardSequence
                || !Clipboard.ContainsText(TextDataFormat.UnicodeText)
                || !string.Equals(Clipboard.GetText(TextDataFormat.UnicodeText), text, StringComparison.Ordinal))
                return new DirectTypingResult(false, "The clipboard changed before insertion. MadNegar left the newer clipboard contents untouched and kept the transcript in the app.");

            if (cancellationToken.IsCancellationRequested)
            {
                var restoredOnCancel = TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
                return new DirectTypingResult(false, "Voice typing was cancelled before the paste was sent." + (restoredOnCancel ? " The previous clipboard was restored." : ""));
            }
            var pasteInputs = new[]
            {
                NativeInput.VirtualKey(VkControl, keyUp: false),
                NativeInput.VirtualKey(VkV, keyUp: false),
                NativeInput.VirtualKey(VkV, keyUp: true),
                NativeInput.VirtualKey(VkControl, keyUp: true)
            };
            var sent = SendInput((uint)pasteInputs.Length, pasteInputs, Marshal.SizeOf<NativeInput>());
            if (sent != pasteInputs.Length)
            {
                // Release modifiers even if Windows accepted only part of the sequence.
                var releaseInputs = new[]
                {
                    NativeInput.VirtualKey(VkV, keyUp: true),
                    NativeInput.VirtualKey(VkControl, keyUp: true)
                };
                _ = SendInput((uint)releaseInputs.Length, releaseInputs, Marshal.SizeOf<NativeInput>());
                if (sent > 0) await Task.Delay(180); // Partial delivery can still have pasted some/all text: do not offer an automatic retry.
                var restored = TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
                var lastError = Marshal.GetLastWin32Error();
                var detail = sent == 0 && lastError == 0
                    ? "Windows did not accept the paste input. The destination may run elevated or be protected by Windows; run both apps at the same permission level and retry, or use Copy Text. MadNegar will not bypass Windows security."
                    : lastError == 0
                        ? "Windows accepted only part of the Ctrl+V shortcut; the result may be partial."
                        : $"Windows accepted only part of the Ctrl+V shortcut (error {lastError}: {new Win32Exception(lastError).Message}); the result may be partial.";
                return new DirectTypingResult(false, detail + (restored ? " The previous clipboard was restored." : " Clipboard contents changed and were left untouched; use Copy Text in MadNegar if needed."), RetrySafe: sent == 0);
            }
            inputDispatched = true;

            // Some applications read the clipboard asynchronously; allow a little more processing time before restoring it.
            await Task.Delay(700);
            var clipboardRestored = TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
            return new DirectTypingResult(true, clipboardRestored
                ? "Text inserted into the original field; the previous clipboard contents were restored."
                : "Text was sent to the original field. The clipboard changed during insertion, so MadNegar left the newer clipboard contents untouched.");
        }
        catch (OperationCanceledException)
        {
            var restored = clipboardWasSet && !inputDispatched && TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
            return new DirectTypingResult(false, "Voice typing was cancelled." + (restored ? " The previous clipboard was restored." : inputDispatched ? " The paste had already been sent and cannot be undone automatically." : ""), RetrySafe: !inputDispatched);
        }
        catch (Exception ex)
        {
            var restored = clipboardWasSet && !inputDispatched && TryRestoreClipboard(previousClipboard, injectedClipboardSequence);
            return new DirectTypingResult(false, $"Text insertion failed ({ex.Message}). The transcript remains in MadNegar." +
                (restored ? " The previous clipboard was restored." : string.Empty), RetrySafe: !inputDispatched);
        }
    }

    private static bool TryRestoreClipboard(IDataObject? previousClipboard, uint injectedSequence)
    {
        try
        {
            if (injectedSequence == 0 || GetClipboardSequenceNumber() != injectedSequence)
                return false; // A later copy/cut operation must never be overwritten by our restore.
            if (previousClipboard is null) Clipboard.Clear();
            else Clipboard.SetDataObject(previousClipboard, copy: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsExplorerWindowClass(string className) =>
        className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase)
        || className.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase)
        || className.Equals("Progman", StringComparison.OrdinalIgnoreCase)
        || className.Equals("WorkerW", StringComparison.OrdinalIgnoreCase);

    private static bool IsEditableControlClass(string className) =>
        className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
        || className.Contains("RichEdit", StringComparison.OrdinalIgnoreCase)
        || className.Equals("DirectUIHWND_Edit", StringComparison.OrdinalIgnoreCase);

    private static bool IsClearlyNonTextControlClass(string className) =>
        className.Equals("Button", StringComparison.OrdinalIgnoreCase)
        || className.Equals("ToolbarWindow32", StringComparison.OrdinalIgnoreCase)
        || className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase)
        || className.Equals("SysTreeView32", StringComparison.OrdinalIgnoreCase)
        || className.Equals("ListBox", StringComparison.OrdinalIgnoreCase)
        || className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)
        || className.Equals("ComboLBox", StringComparison.OrdinalIgnoreCase)
        || className.Equals("#32768", StringComparison.OrdinalIgnoreCase);

    private static string GetWindowClassName(IntPtr window)
    {
        var builder = new StringBuilder(256);
        _ = GetClassName(window, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;
        var normalized = token.Trim();
        if (normalized.Equals("Spacebar", StringComparison.OrdinalIgnoreCase)) normalized = "Space";
        if (normalized.Equals("Esc", StringComparison.OrdinalIgnoreCase)) normalized = "Escape";
        if (normalized.Equals("Return", StringComparison.OrdinalIgnoreCase)) normalized = "Enter";
        if (normalized.Equals("PgUp", StringComparison.OrdinalIgnoreCase)) normalized = "PageUp";
        if (normalized.Equals("PgDn", StringComparison.OrdinalIgnoreCase)) normalized = "PageDown";
        if (normalized.Length == 1 && char.IsDigit(normalized[0])) normalized = "D" + normalized;
        if (!Enum.TryParse(normalized, ignoreCase: true, out key) || !Enum.IsDefined(key)) return false;
        key &= Keys.KeyCode;
        return key != Keys.None;
    }

    private static bool IsModifierKey(Keys key) => key is Keys.ControlKey or Keys.Control or Keys.ShiftKey or Keys.Shift
        or Keys.Menu or Keys.Alt or Keys.LWin or Keys.RWin;

    private static string FormatKey(Keys key)
    {
        if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
        return key switch
        {
            Keys.Space => "Space",
            Keys.Escape => "Esc",
            Keys.Enter => "Enter",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.Oemcomma => "OemComma",
            Keys.OemPeriod => "OemPeriod",
            _ => key.ToString()
        };
    }

    private static IntPtr GetFocusedWindow(uint threadId, IntPtr fallback)
    {
        if (threadId == 0) return fallback;
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref info) && info.FocusWindow != IntPtr.Zero
            ? info.FocusWindow
            : fallback;
    }

    [StructLayout(LayoutKind.Sequential, Size = 40)]
    private struct NativeInput
    {
        public uint Type;
        public NativeKeyboardInput Keyboard;

        public static NativeInput VirtualKey(int value, bool keyUp) => new()
        {
            Type = InputKeyboard,
            Keyboard = new NativeKeyboardInput
            {
                VirtualKey = (ushort)value,
                ScanCode = 0,
                Flags = keyUp ? KeyEventKeyUp : 0,
                Time = 0,
                ExtraInfo = IntPtr.Zero
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr ActiveWindow;
        public IntPtr FocusWindow;
        public IntPtr CaptureWindow;
        public IntPtr MenuOwnerWindow;
        public IntPtr MoveSizeWindow;
        public IntPtr CaretWindow;
        public NativeRect CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, NativeInput[] inputs, int sizeOfInput);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
