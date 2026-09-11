using System.Windows.Forms;

namespace DshLauncher.Services
{
    /// <summary>
    /// Desktop notifications via the tray icon's balloon tip (zero-dependency).
    /// </summary>
    public class Notifier
    {
        private readonly NotifyIcon _icon;

        public Notifier(NotifyIcon icon)
        {
            _icon = icon;
        }

        public void Notify(string title, string message)
        {
            _icon.Visible = true;
            _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
        }
    }
}
