using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.InteropServices;

using MyPrinter.Desktop.Activation;

namespace MyPrinter.Desktop;

public partial class MainForm : Form
{
    private static readonly TimeSpan RuntimeLicenseCheckInterval = TimeSpan.FromMinutes(1);

    private WebView2 _webView = null!;
    private NotifyIcon _trayIcon = null!;
    private readonly List<Image> _ownedTrayImages = new();
    private Icon? _windowIcon;
    private Icon? _trayNotifyIcon;
    private RuntimeLicenseMonitor? _runtimeLicenseMonitor;
    private ActivationForm? _runtimeActivationForm;
    private int _runtimeActivationInProgress;
    private bool _reallyExit = false;

    public MainForm()
    {
        InitializeComponent();
        SetupWindow();
        SetupTray();
        SetupWebView();
        SetupRuntimeLicenseMonitor();
    }

    // ── Window chrome ──────────────────────────────────────────
    private void SetupWindow()
    {
        Text          = "🖨 Máy In Thông Minh | Smart Printer";
        WindowState   = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize   = new Size(900, 600);
        BackColor     = Color.FromArgb(15, 23, 42);
        ShowInTaskbar = true;
        _windowIcon   = CreatePrinterIcon();
        Icon          = _windowIcon;
    }

    // ── System Tray ────────────────────────────────────────────
    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Renderer = new TrayMenuRenderer();
        menu.Font = new Font("Segoe UI", 10.5f, FontStyle.Regular);

        var openItem = new ToolStripMenuItem(
            "Open  —  Smart Printer",
            CreateOwnedMenuBitmap(Color.FromArgb(16, 185, 129), "+"),
            (_, _) => ShowWindow());
        openItem.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(
            "Hide",
            CreateOwnedMenuBitmap(Color.FromArgb(100, 116, 139), "-"),
            (_, _) => HideWindow()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(
            "Exit",
            CreateOwnedMenuBitmap(Color.FromArgb(239, 68, 68), "x"),
            (_, _) => ExitApp()));

        _trayNotifyIcon = CreatePrinterIcon();
        _trayIcon = new NotifyIcon
        {
            Icon             = _trayNotifyIcon,
            Text             = "Smart Printer",
            ContextMenuStrip = menu,
            Visible          = true,
        };

        _trayIcon.Click += (_, e) =>
        {
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
            {
                if (Visible && WindowState != FormWindowState.Minimized)
                    HideWindow();
                else
                    ShowWindow();
            }
        };

        _trayIcon.DoubleClick += (_, _) => ShowWindow();

        _trayIcon.BalloonTipTitle = "Smart Printer";
        _trayIcon.BalloonTipText  = "App is running. Click the icon to open.";
        _trayIcon.ShowBalloonTip(2000);
    }

    private Bitmap CreateOwnedMenuBitmap(Color color, string symbol)
    {
        var bitmap = CreateMenuBitmap(color, symbol);
        _ownedTrayImages.Add(bitmap);
        return bitmap;
    }


    private static Bitmap CreateMenuBitmap(Color color, string symbol)
    {
        const int S = 20;
        var bmp = new Bitmap(S, S, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, S - 2, S - 2);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(symbol, font, textBrush, new RectangleF(0, 0, S, S), sf);
        return bmp;
    }

    private class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer() : base(new TrayColorTable()) { }
    }

    private class TrayColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected              => Color.FromArgb(232, 248, 244);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(232, 248, 244);
        public override Color MenuItemSelectedGradientEnd   => Color.FromArgb(209, 243, 233);
        public override Color MenuItemBorder                => Color.FromArgb(16, 185, 129);
        public override Color MenuBorder                    => Color.FromArgb(220, 220, 220);
        public override Color ToolStripDropDownBackground   => Color.White;
    }

    private void ShowWindow()
    {
        Show();
        WindowState   = FormWindowState.Maximized;
        ShowInTaskbar = true;
        Activate();
        BringToFront();
    }

    public void ShowFromExternalActivation()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowFromExternalActivation);
            return;
        }

        if (_runtimeActivationForm is { IsDisposed: false, Visible: true })
        {
            _runtimeActivationForm.WindowState = FormWindowState.Normal;
            _runtimeActivationForm.Show();
            _runtimeActivationForm.Activate();
            _runtimeActivationForm.BringToFront();
            SetForegroundWindow(_runtimeActivationForm.Handle);
            return;
        }

        ShowWindow();
        SetForegroundWindow(Handle);
    }

    private void HideWindow()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        _runtimeLicenseMonitor?.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    private void SetupRuntimeLicenseMonitor()
    {
        _runtimeLicenseMonitor = new RuntimeLicenseMonitor(
            () => LicenseGuard.IsActivated(),
            RuntimeLicenseCheckInterval,
            () =>
            {
                if (IsDisposed)
                    return;

                if (InvokeRequired)
                    BeginInvoke(new Action(HandleRuntimeLicenseInvalidAsync));
                else
                    HandleRuntimeLicenseInvalidAsync();
            });

        _runtimeLicenseMonitor.Start();
    }

    private void HandleRuntimeLicenseInvalidAsync()
    {
        if (IsDisposed)
            return;
        if (Interlocked.Exchange(ref _runtimeActivationInProgress, 1) == 1)
            return;

        try
        {
            _runtimeLicenseMonitor?.Stop();
            HideWindow();

            using var activationForm = new ActivationForm();
            _runtimeActivationForm = activationForm;
            var dialogResult = activationForm.ShowDialog(this);

            if (dialogResult == DialogResult.OK && activationForm.Activated && LicenseGuard.IsActivated())
            {
                _runtimeActivationForm = null;
                _runtimeLicenseMonitor?.Restart();
                ShowFromExternalActivation();
                return;
            }

            _runtimeActivationForm = null;
            ExitApp();
        }
        finally
        {
            _runtimeActivationForm = null;
            Interlocked.Exchange(ref _runtimeActivationInProgress, 0);
        }
    }

    // ── Intercept X button → hide instead of close ─────────────
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideWindow();
            return;
        }

        base.OnFormClosing(e);
    }

    // ── Tray icon: modern 3D printer in white/green ────────────
    private static Icon CreatePrinterIcon()
    {
        const int S = 32;
        using var bmp = new Bitmap(S, S, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode      = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode    = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.InterpolationMode  = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        // ── 1. Rounded-rect green gradient background ────────────
        using var bgPath = RoundedRect(0, 0, S, S, 7);
        using var bgGrad = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Point(0, 0), new Point(S, S),
            Color.FromArgb(255, 16, 185, 129),   // emerald-500
            Color.FromArgb(255,  5, 150, 105));   // emerald-600
        g.FillPath(bgGrad, bgPath);

        // Subtle glossy highlight on top-left quadrant
        using var glossPath = RoundedRect(1, 1, S - 2, S / 2, 6);
        using var glossBrush = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
        g.FillPath(glossBrush, glossPath);

        // ── 2. Shadow under printer body (3D depth) ──────────────
        using var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0));
        using var shadowPath = RoundedRect(5, 14, 22, 11, 3);
        g.FillPath(shadowBrush, shadowPath);

        // ── 3. Printer body (white with subtle gradient) ─────────
        using var bodyGrad = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(6, 12, 20, 10),
            Color.FromArgb(255, 255, 255, 255),
            Color.FromArgb(255, 230, 240, 235),
            System.Drawing.Drawing2D.LinearGradientMode.Vertical);
        using var bodyPath = RoundedRect(6, 12, 20, 10, 3);
        g.FillPath(bodyGrad, bodyPath);

        // Body outline
        using var outlinePen = new Pen(Color.FromArgb(80, 0, 80, 50), 0.8f);
        g.DrawPath(outlinePen, bodyPath);

        // ── 4. Paper input tray (top) ────────────────────────────
        using var paperBrush = new SolidBrush(Color.FromArgb(255, 245, 250, 248));
        using var paperPath = RoundedRect(9, 6, 14, 8, 2);
        g.FillPath(paperBrush, paperPath);
        // Paper edge lines
        using var paperLinePen = new Pen(Color.FromArgb(60, 16, 185, 129), 0.6f);
        g.DrawLine(paperLinePen, 12, 8, 20, 8);
        g.DrawLine(paperLinePen, 12, 10, 19, 10);

        // ── 5. Paper output slot (dark strip on body) ────────────
        using var slotBrush = new SolidBrush(Color.FromArgb(255, 5, 150, 105));
        g.FillRectangle(slotBrush, 9, 16, 14, 2);

        // ── 6. Output paper coming out ───────────────────────────
        using var outPaperBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
        using var outPaperPath = RoundedRect(10, 19, 12, 6, 1);
        g.FillPath(outPaperBrush, outPaperPath);
        // Tiny text lines on output paper
        using var textLinePen = new Pen(Color.FromArgb(80, 16, 185, 129), 0.5f);
        g.DrawLine(textLinePen, 12, 21, 18, 21);
        g.DrawLine(textLinePen, 12, 23, 17, 23);

        // ── 7. Power indicator (small bright dot) ────────────────
        using var dotBrush = new SolidBrush(Color.FromArgb(255, 52, 211, 153)); // bright green
        g.FillEllipse(dotBrush, 22, 14, 2.5f, 2.5f);

        var nativeIconHandle = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(nativeIconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(nativeIconHandle);
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr nativeIconHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // ── GDI+ helper: rounded rectangle path ─────────────────────
    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ── WebView2 setup ─────────────────────────────────────────
    private void SetupWebView()
    {
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
        _webView.CoreWebView2InitializationCompleted += OnWebViewReady;
        InitWebViewAsync();
    }

    private async void InitWebViewAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyPrinter", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await _webView.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không thể khởi tạo WebView2.\n\n" +
                $"Hãy đảm bảo Microsoft Edge WebView2 Runtime đã được cài đặt.\n\n" +
                $"Tải tại: https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                $"Chi tiết lỗi: {ex.Message}",
                "Lỗi WebView2",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitApp();
        }
    }

    private void OnWebViewReady(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            MessageBox.Show(
                $"WebView2 initialization failed: {e.InitializationException?.Message}",
                "Lỗi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var wv = _webView.CoreWebView2;

        var settings = wv.Settings;
        settings.IsStatusBarEnabled               = false;
        settings.AreDefaultContextMenusEnabled    = false;
        settings.IsZoomControlEnabled             = false;
        settings.AreBrowserAcceleratorKeysEnabled = true; // re-enabled so F12 opens DevTools
        settings.IsSwipeNavigationEnabled         = false;
        settings.AreDevToolsEnabled               = true; // set false for release

        var frontendPath = GetFrontendPath();
        wv.SetVirtualHostNameToFolderMapping(
            "app.local",
            frontendPath,
            CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.Navigate($"https://app.local/index.html?port={Program.BackendPort}");
        _webView.ZoomFactor = 1.1;  // slightly larger for readability

        // Sync WinForms title bar when JS changes document.title (e.g. language switch)
        wv.DocumentTitleChanged += (s, _) => {
            if (InvokeRequired) Invoke(() => Text = wv.DocumentTitle);
            else Text = wv.DocumentTitle;
        };


        wv.NewWindowRequested += (s, args) => args.Handled = true;

    }

    // ── Frontend path resolution ───────────────────────────────
    private static string GetFrontendPath()
    {
        var exeDir    = AppContext.BaseDirectory;

#if DEBUG
        // In Debug mode: prefer the source frontend/ folder so edits are
        // reflected immediately without copying to bin/Debug each time.
        var devPath = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "frontend"));
        if (Directory.Exists(devPath)) return devPath;
#endif

        var candidate = Path.Combine(exeDir, "frontend");
        if (Directory.Exists(candidate)) return candidate;

#if !DEBUG
        var devPath2 = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "frontend"));
        if (Directory.Exists(devPath2)) return devPath2;
#endif

        var checkedPath = Path.Combine(exeDir, "frontend");
        throw new DirectoryNotFoundException($"Frontend folder not found. Checked:\n  {checkedPath}");
    }
}

