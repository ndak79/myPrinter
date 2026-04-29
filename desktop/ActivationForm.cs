using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyPrinter.Desktop.Activation;

namespace MyPrinter.Desktop;

public partial class ActivationForm : Form
{
    public new bool Activated { get; private set; }
    private readonly string _fingerprint;

    public ActivationForm()
    {
        InitializeComponent();
        _fingerprint    = LicenseGuard.GetFingerprint();
        lblFpValue.Text = _fingerprint;
    }

    private const string FingerprintLabelDefault =
        "Mã thiết bị / Device Fingerprint (Click để copy):";

    private void CopyFingerprint()
    {
        // Guard Clipboard.SetText — clipboard contention (locked by another process) throws on Windows.
        // Mirrors Python copy_fp() which calls clipboard APIs unguarded; C# adds safety here
        // since an unhandled exception in a click handler can fault the entire activation dialog.
        try
        {
            Clipboard.SetText(_fingerprint);
        }
        catch (Exception)
        {
            SetStatus("❌ Không thể copy fingerprint vào Clipboard. Vui lòng thử lại.", error: true);
            return;
        }

        // Use constant instead of capturing current label text — fixes rapid-click race:
        // two fast clicks create two timers; if the second captures the already-mutated
        // success text, the last reset permanently leaves the label in "copied" state.
        // Mirrors Python copy_fp() which always resets to the fixed default string.
        lblFingerprint.Text      = "Mã thiết bị (✅ Đã copy vào Clipboard!):";
        lblFingerprint.ForeColor = Color.FromArgb(40, 167, 69);

        _ = Task.Delay(2000).ContinueWith(_ =>
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    lblFingerprint.Text      = FingerprintLabelDefault;
                    lblFingerprint.ForeColor = Color.FromArgb(85, 85, 85);
                }));
            }
            catch { }
        }, TaskScheduler.Default);
    }

    private async Task OnActivateClickedAsync()
    {
        var key = txtKey.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            SetStatus("⚠️ Vui lòng nhập mã kích hoạt.", error: true);
            return;
        }

        btnActivate.Enabled  = false;
        btnImportLic.Enabled = false;
        btnActivate.Text     = "Đang kích hoạt...";
        SetStatus("Đang kết nối máy chủ...", error: false);

        bool ok;
        string? err;
        try
        {
            (ok, err) = await LicenseGuard.ActivateOnlineAsync(key);
        }
        catch (Exception ex)
        {
            ok  = false;
            err = ex.Message;
        }

        if (ok)
        {
            // Success: set Activated + DialogResult BEFORE delay so X-button close during
            // the 800ms cosmetic pause still returns DialogResult.OK to ShowDialog().
            Activated    = true;
            DialogResult = DialogResult.OK;
            SetStatus("✅ Kích hoạt thành công!", error: false);
            await Task.Delay(800);
            Close();
        }
        else
        {
            // Failure: re-enable so user can retry
            btnActivate.Enabled  = true;
            btnImportLic.Enabled = true;
            btnActivate.Text     = "Kích hoạt Online";
            SetStatus($"❌ Kích hoạt thất bại: {err}", error: true);
        }
    }

    // Async to match ActivateOfflineAsync (NTP + heartbeat should not block UI thread)
    private async Task OnImportLicClickedAsync()
    {
        using var ofd = new OpenFileDialog
        {
            Title  = "Chọn file .lic",
            Filter = "License files (*.lic)|*.lic|All files (*.*)|*.*",
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        btnActivate.Enabled  = false;
        btnImportLic.Enabled = false;
        SetStatus("Đang kiểm tra file .lic...", error: false);

        bool ok;
        try   { ok = await LicenseGuard.ActivateOfflineAsync(ofd.FileName); }
        catch { ok = false; }

        if (ok)
        {
            // Success: set Activated + DialogResult BEFORE delay so X-button close during
            // the 800ms cosmetic pause still returns DialogResult.OK to ShowDialog().
            Activated    = true;
            DialogResult = DialogResult.OK;
            SetStatus("✅ Import file .lic thành công!", error: false);
            await Task.Delay(800);
            Close();
        }
        else
        {
            // Failure: re-enable so user can retry
            btnActivate.Enabled  = true;
            btnImportLic.Enabled = true;
            SetStatus("❌ File .lic không hợp lệ hoặc không khớp thiết bị này.", error: true);
        }
    }

    private void SetStatus(string msg, bool error)
    {
        lblStatus.Text      = msg;
        lblStatus.ForeColor = error
            ? Color.FromArgb(220, 53, 69)
            : Color.FromArgb(40, 167, 69);
    }
}
