#nullable enable
namespace MyPrinter.Desktop;

partial class ActivationForm
{
    private System.ComponentModel.IContainer? components = null;
    private Label   lblTitle = null!, lblFingerprint = null!, lblKey = null!, lblStatus = null!;
    private Button  btnActivate = null!, btnImportLic = null!;
    private TextBox txtKey = null!;
    private Panel   pnlFingerprint = null!;
    private Label   lblFpValue = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.Text            = "myPrinter — Kích hoạt phần mềm";
        this.Size            = new Size(620, 440);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox     = false;
        this.MinimizeBox     = false;
        this.StartPosition   = FormStartPosition.CenterScreen;
        this.BackColor       = Color.White;
        this.Font            = new Font("Segoe UI", 10F);

        lblTitle = new Label
        {
            Text      = "🔐 Kích hoạt myPrinter",
            Font      = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 83, 141),
            Location  = new Point(30, 25),
            Size      = new Size(560, 40),
            AutoSize  = false,
        };

        pnlFingerprint = new Panel
        {
            BackColor   = Color.FromArgb(240, 240, 240),
            Location    = new Point(30, 75),
            Size        = new Size(555, 85),
            BorderStyle = BorderStyle.None,
        };

        lblFingerprint = new Label
        {
            Text      = "Mã thiết bị / Device Fingerprint (Click để copy):",
            ForeColor = Color.FromArgb(85, 85, 85),
            Location  = new Point(10, 10),
            Size      = new Size(530, 20),
            AutoSize  = false,
        };

        lblFpValue = new Label
        {
            Text      = "",
            Font      = new Font("Consolas", 10F),
            ForeColor = Color.FromArgb(50, 50, 50),
            Location  = new Point(10, 35),
            Size      = new Size(535, 38),
            Cursor    = Cursors.Hand,
            AutoSize  = false,
        };
        lblFpValue.Click += (_, _) => CopyFingerprint();

        pnlFingerprint.Controls.AddRange([lblFingerprint, lblFpValue]);

        lblKey = new Label
        {
            Text     = "Nhập mã kích hoạt (Activation Key):",
            Location = new Point(30, 175),
            Size     = new Size(560, 22),
            AutoSize = false,
        };

        txtKey = new TextBox
        {
            Font            = new Font("Consolas", 11F),
            Location        = new Point(30, 200),
            Size            = new Size(555, 30),
            PlaceholderText = "Nhập mã kích hoạt tại đây...",
        };

        btnActivate = new Button
        {
            Text      = "Kích hoạt Online",
            Font      = new Font("Segoe UI", 11F, FontStyle.Bold),
            Location  = new Point(30, 255),
            Size      = new Size(200, 40),
            BackColor = Color.FromArgb(31, 83, 141),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
        };
        btnActivate.FlatAppearance.BorderSize = 0;
        btnActivate.Click += async (_, _) => await OnActivateClickedAsync();

        btnImportLic = new Button
        {
            Text      = "Import file .lic (Offline)",
            Font      = new Font("Segoe UI", 10F),
            Location  = new Point(245, 255),
            Size      = new Size(200, 40),
            BackColor = Color.FromArgb(108, 117, 125),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
        };
        btnImportLic.FlatAppearance.BorderSize = 0;
        btnImportLic.Click += async (_, _) => await OnImportLicClickedAsync();

        lblStatus = new Label
        {
            Text      = "",
            ForeColor = Color.FromArgb(220, 53, 69),
            Location  = new Point(30, 310),
            Size      = new Size(555, 80),
            AutoSize  = false,
        };

        this.Controls.AddRange([
            lblTitle, pnlFingerprint, lblKey, txtKey,
            btnActivate, btnImportLic, lblStatus
        ]);
    }
}
