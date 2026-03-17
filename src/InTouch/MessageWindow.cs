using System;
using System.Windows.Forms;

namespace InTouch
{
    /// <summary>
    /// A hidden message-only window that receives WM_HOTKEY messages
    /// and forwards them to a callback.
    /// </summary>
    internal sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly Action _onHotkey;

        public MessageWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;

            var cp = new CreateParams
            {
                Caption = "InTouch_MessageWindow",
                // HWND_MESSAGE parent makes this a message-only window
                Parent = new IntPtr(-3)
            };
            CreateHandle(cp);

            NativeMethods.RegisterHotKey(
                Handle,
                NativeMethods.HOTKEY_ID,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                NativeMethods.VK_T);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == NativeMethods.HOTKEY_ID)
            {
                _onHotkey();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            NativeMethods.UnregisterHotKey(Handle, NativeMethods.HOTKEY_ID);
            DestroyHandle();
        }
    }
}
