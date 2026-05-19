namespace MyPrinter.Desktop;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runtimeLicenseMonitor?.Dispose();
            _webView?.Dispose();
            if (_trayIcon != null)
                _trayIcon.Visible = false;
            _trayIcon?.Dispose();
            foreach (var image in _ownedTrayImages)
                image.Dispose();
            _ownedTrayImages.Clear();
            _windowIcon?.Dispose();
            _trayNotifyIcon?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 800);
        Text = "Máy In Thông Minh";
    }
}
