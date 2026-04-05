using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MyPrinter.Desktop;

public partial class MainForm : Form
{
    private WebView2 _webView = null!;
    private NotifyIcon _trayIcon = null!;
    private bool _reallyExit = false;

    public MainForm()
    {
        InitializeComponent();
        SetupWindow();
        SetupTray();
        SetupWebView();
    }

    // ── Window chrome ──────────────────────────────────────────
    private void SetupWindow()
    {
        Text          = "🖨️ Máy In Thông Minh";
        WindowState   = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize   = new Size(900, 600);
        BackColor     = Color.FromArgb(15, 23, 42);
        ShowInTaskbar = true;
    }

    // ── System Tray ────────────────────────────────────────────
    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mở Máy In Thông Minh", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon             = CreatePrinterIcon(),
            Text             = "Máy In Thông Minh",
            ContextMenuStrip = menu,
            Visible          = true,
        };

        // Single click → show/hide toggle
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

        // Double-click → always show
        _trayIcon.DoubleClick += (_, _) => ShowWindow();

        _trayIcon.BalloonTipTitle = "Máy In Thông Minh";
        _trayIcon.BalloonTipText  = "Ứng dụng đang chạy. Click vào icon để mở.";
        _trayIcon.ShowBalloonTip(2000);
    }

    private void ShowWindow()
    {
        Show();
        WindowState   = FormWindowState.Maximized;
        ShowInTaskbar = true;
        Activate();
        BringToFront();
    }

    private void HideWindow()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
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

        _webView?.Dispose();
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

        // Convert Bitmap → Icon
        return Icon.FromHandle(bmp.GetHicon());
    }

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

        wv.NewWindowRequested += (s, args) => args.Handled = true;
    }

    // ── Frontend path resolution ───────────────────────────────
    private static string GetFrontendPath()
    {
        var exeDir    = AppContext.BaseDirectory;
        var candidate = Path.Combine(exeDir, "frontend");
        if (Directory.Exists(candidate)) return candidate;

        var devPath = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "frontend"));
        if (Directory.Exists(devPath)) return devPath;

        throw new DirectoryNotFoundException(
            $"Frontend folder not found. Checked:\n  {candidate}\n  {devPath}");
    }
}
