using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EventFlowStudio
{
    // Schaltet die Windows-Titelleiste auf dunkel, passend zum App-Theme.
    // Funktioniert ab Windows 10 20H1 und Windows 11; ältere Versionen ignorieren den Aufruf.
    public partial class MainWindow
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int enabled = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref enabled, sizeof(int));

                // Windows 11: Titelleiste exakt in der Hintergrundfarbe (#0B0E14, als 0x00BBGGRR)
                int captionColor = 0x00140E0B;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            }
            catch
            {
                // Rein kosmetisch – bei Fehlern einfach die Standard-Titelleiste behalten.
            }
        }
    }
}
