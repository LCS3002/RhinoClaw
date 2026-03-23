using System;
using Eto.Forms;
using Rhino.UI;

namespace PenguinClaw
{
    [System.Runtime.InteropServices.Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
    public class PenguinClawPanel : Panel, IPanel
    {
        private WebView _webView;
        private DateTime _lastReload = DateTime.MinValue;

        public static Guid PanelId => new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        public PenguinClawPanel()
        {
            _webView = new WebView();
            try
            {
                PenguinClawServer.StartServer();
                _webView.Url = new Uri("http://localhost:8080");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PenguinClaw panel error: {ex.Message}");
            }
            Content = _webView;

            // WebView2 on Windows drops its rendering context on focus/visibility changes.
            // The GotFocus hook is Windows-only; on macOS the Eto WebView handles this natively.
            // PanelShown (below) covers tab-switching on all platforms.
#if !__MACOS__
            try
            {
                var mainWindow = RhinoEtoApp.MainWindow;
                if (mainWindow != null)
                    mainWindow.GotFocus += OnMainWindowGotFocus;
            }
            catch { }
#endif
        }

        private void OnMainWindowGotFocus(object sender, EventArgs e)
        {
            // 1-second cooldown prevents reload loops when focus bounces
            if ((DateTime.Now - _lastReload).TotalSeconds < 1) return;
            if (!Visible) return;
            ReloadWebView();
        }

        private void ReloadWebView()
        {
            _lastReload = DateTime.Now;
            // Append timestamp so Eto always sees a new URL and triggers a real navigation.
            // The server ignores query strings (AbsolutePath matching), React preserves
            // state via localStorage — so chat history survives the reload.
            Application.Instance.AsyncInvoke(() =>
            {
                try
                {
                    var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _webView.Url = new Uri($"http://localhost:8080/?_={ts}");
                }
                catch { }
            });
        }

        public static System.Drawing.Icon PanelIcon => PenguinClawIconFactory.CreateIcon(32);

        public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
        {
            PenguinClawServer.StartServer();
            ReloadWebView();
        }

        public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
        {
            // Keep running in background
        }

        public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
        {
#if !__MACOS__
            try
            {
                var mainWindow = RhinoEtoApp.MainWindow;
                if (mainWindow != null)
                    mainWindow.GotFocus -= OnMainWindowGotFocus;
            }
            catch { }
#endif
            try { PenguinClawServer.StopServer(); } catch { }
        }
    }
}
