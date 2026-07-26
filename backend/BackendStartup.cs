using PrinterApp.Models;
using PrinterApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using System.Diagnostics;

namespace PrinterApp;

/// <summary>
/// Centralised backend startup — can be called both from the standalone
/// CLI entry point (Program.cs) and from the WinForms desktop host.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BackendStartup
{
    private const long MaxUploadFileBytes = 100L * 1024 * 1024;
    private const long MultipartRequestOverheadBytes = 1024 * 1024;

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = MaxUploadFileBytes + MultipartRequestOverheadBytes;
        });

        // Configure JSON serialization: enums as strings so "CW90", "CCW90", etc.
        // in PageRotations are correctly deserialized into RotationDirection enum values.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // Configure CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("LocalWebApp", policy =>
            {
                policy.SetIsOriginAllowed(IsAllowedLocalFrontendOrigin)
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // Configure file upload limits
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = MaxUploadFileBytes;
        });

        // Register services
        builder.Services.AddSingleton<PrinterManagementService>();
        builder.Services.AddSingleton<IWordInteropService, WordInteropService>();
        builder.Services.AddSingleton<PrintAlgorithmService>();
        builder.Services.AddSingleton<FileSessionService>();

        var app = builder.Build();

        app.UseCors("LocalWebApp");

        // BUG-8-5 fix: global fallback exception handler so that any unhandled exception
        // (e.g. from middleware or outside route try/catch blocks) returns a JSON body
        // that the frontend can parse, rather than a plain-text Kestrel 500.
        app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
        {
            ctx.Response.StatusCode  = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new PrintResponse
            {
                Success = false,
                Message = "An unexpected server error occurred. Please try again."
            });
        }));

        // ── API Endpoints ──────────────────────────────────────────────

        app.MapGet("/api/printers", (PrinterManagementService printerService) =>
        {
            try
            {
                var printers = printerService.GetAllPrinters();
                return Results.Ok(printers);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error getting printers: {ex.Message}");
            }
        });

        app.MapPost("/api/upload", async (HttpRequest request, IWebHostEnvironment env, FileSessionService sessions) =>
        {
            try
            {
                if (!request.HasFormContentType || request.Form.Files.Count == 0)
                    return Results.BadRequest(new UploadResponse { Success = false, Message = "No file uploaded" });

                var file = request.Form.Files[0];
                var extension = Path.GetExtension(file.FileName).ToLower();

                var allowedExtensions = new[] { ".doc", ".docx", ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp" };
                if (!allowedExtensions.Contains(extension))
                    return Results.BadRequest(new UploadResponse
                    {
                        Success = false,
                        Message = $"File type not supported. Allowed: {string.Join(", ", allowedExtensions)}"
                    });

                var fileId = Guid.NewGuid().ToString();
                var tempPath = Path.Combine(Path.GetTempPath(), $"{fileId}{extension}");

                using (var stream = new FileStream(tempPath, FileMode.Create))
                    await file.CopyToAsync(stream);

                var fileInfo = new FileInfo(tempPath);
                if (fileInfo.Length == 0)
                    return Results.BadRequest(new UploadResponse { Success = false, Message = "Uploaded file is empty" });

                sessions.AddFile(fileId, tempPath);

                return Results.Ok(new UploadResponse
                {
                    Success = true,
                    FileId = fileId,
                    OriginalFileName = file.FileName
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error uploading file: {ex.Message}");
            }
        });

        app.MapGet("/api/file/{fileId}", (string fileId, FileSessionService sessions, HttpContext ctx) =>
        {
            try
            {
                var filePath = sessions.GetFilePath(fileId);
                if (filePath == null) return Results.NotFound("File not found");
                if (!File.Exists(filePath)) return Results.NotFound("File not found on disk");

                ctx.Response.Headers["Cache-Control"] = "private, max-age=3600";
                ctx.Response.Headers["Accept-Ranges"]  = "bytes";

                return Results.File(
                    path:                  filePath,
                    contentType:           "application/pdf",
                    fileDownloadName:      null,
                    lastModified:          File.GetLastWriteTimeUtc(filePath),
                    entityTag:             null,
                    enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error serving file: {ex.Message}");
            }
        });

        app.MapPost("/api/convert", (
            string fileId,
            IWordInteropService wordService,
            FileSessionService sessions) =>
        {
            try
            {
                var filePath = sessions.GetFilePath(fileId);
                if (filePath == null) return Results.NotFound("File not found");

                var extension = Path.GetExtension(filePath).ToLower();
                var pdfPath = Path.ChangeExtension(filePath, ".pdf");

                if (extension == ".pdf")
                    return Results.Ok(new { pdfPath });
                else if (extension is ".doc" or ".docx")
                    wordService.ConvertToPdf(filePath, pdfPath);
                else if (extension is ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp" or ".webp")
                    wordService.ConvertImageToPdf(filePath, pdfPath);
                else
                    return Results.BadRequest($"Unsupported file type: {extension}");

                sessions.UpdateFilePath(fileId, pdfPath);
                // BE-20-3 fix: delete the original uploaded file (Word/image) now that
                // the session tracks only the converted pdfPath. Without this, the original
                // temp file is orphaned: session cleanup deletes pdfPath but never knows
                // about filePath (which has already been superseded).
                FileSessionService.DeleteFileSafe(filePath);
                return Results.Ok(new { success = true, pdfPath });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error converting file: {ex.Message}");
            }
        });

        app.MapPost("/api/print", async (
            PrintRequest request,
            PrinterManagementService printerService,
            IWordInteropService wordService,
            PrintAlgorithmService printAlgorithm,
            FileSessionService sessions) =>
        {
            try
            {
                // BUG-1 fix: explicit null/empty guards before any dictionary lookup
                if (string.IsNullOrWhiteSpace(request.FileId))
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "fileId is required." });
                if (string.IsNullOrWhiteSpace(request.PrinterName))
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "printerName is required." });

                var filePath = sessions.GetFilePath(request.FileId);
                if (filePath == null)
                    return Results.NotFound(new PrintResponse { Success = false, Message = "File not found. Please upload again." });

                var extension = Path.GetExtension(filePath).ToLower();
                if (extension != ".pdf")
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "File must be converted to PDF first." });

                if (!File.Exists(filePath))
                    return Results.NotFound(new PrintResponse { Success = false, Message = "File not found on server. Please upload again." });

                // BUG-5 fix: collapse double WMI query into one; use OrdinalIgnoreCase consistently
                var printers = printerService.GetAllPrinters();
                var printer = printers.FirstOrDefault(p =>
                    string.Equals(p.Name, request.PrinterName, StringComparison.OrdinalIgnoreCase));
                if (printer == null)
                    return Results.NotFound(new PrintResponse { Success = false, Message = "Printer not found." });
                // BUG-4D fix: use already-retrieved printer.Status instead of firing a second WMI query
                if (printer.Status == PrinterStatus.Offline)
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "Printer is not available." });

                PrintJobState jobState;

                // BUG-2 fix: explicit else-if for each mode + reject unknown values
                if (request.Mode == PrintMode.NormalDuplex)
                    jobState = printAlgorithm.CreateNormalDuplexJob(filePath, request.PrinterName, printer.IsDuplex,
                        request.PageRange, request.SingleSidedPages, request.PageOrder, request.PageRotations,
                        duplexSide:    request.DuplexSide,
                        manualFlipDir: request.ManualFlipDir);
                else if (request.Mode == PrintMode.Simplex)
                    jobState = printAlgorithm.CreateSimplexJob(filePath, request.PrinterName,
                        request.PageRange, request.PageOrder, request.PageRotations);
                else if (request.Mode == PrintMode.BookletA5)
                    jobState = printAlgorithm.CreateBookletJob(filePath, request.PrinterName, printer.IsDuplex,
                        request.PageRange, request.SingleSidedPages, request.PageOrder, request.PageRotations);
                else
                    return Results.BadRequest(new PrintResponse { Success = false, Message = $"Unknown print mode: {(int)request.Mode}" });

                // Propagate printer PPM so ExecutePrintJob can compute the eject delay dynamically.
                jobState.PpmEstimate = printer.PpmEstimate;

                // BUG-8-3 fix: clamp copies to [1, 100] — no upper bound check existed,
                // allowing accidental or malicious requests to loop thousands of times.
                int copies = Math.Clamp(request.Copies, 1, 100);
                jobState.Copies = copies;

                // BUG-6 fix: only store the job in session if it's a manual duplex waiting for flip.
                // Completed non-manual jobs don't need to be stored and would accumulate in memory.
                if (!jobState.IsManualDuplex)
                {
                    for (int copy = 0; copy < copies; copy++)
                    {
                        printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);
                    }
                    // BUG-8-2 fix: clean up intermediate temp files now that print is done
                    FileSessionService.DeleteIntermediateFiles(jobState);
                    // No AddJob — print is complete, nothing to continue
                }
                else
                {
                    // Store before Phase 1 so a partially-sent front pass can still be recovered.
                    sessions.AddJob(jobState.JobId, jobState);

                    // ExecutePrintJob expands manual-duplex copies into a single continuous side pass.
                    printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);
                }

                return Results.Ok(new PrintResponse
                {
                    Success = true,
                    Message = jobState.WaitingForFlip ? "First phase complete. Waiting for manual flip." : "Print job sent successfully.",
                    JobState = jobState.WaitingForFlip ? jobState : null
                });
            }
            catch (InvalidOperationException ex)
            {
                // BUG-7 fix: domain errors (invalid page range, empty PDF, etc.) → 400 not 500
                return Results.BadRequest(new PrintResponse { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error printing: {ex.Message}");
            }
        });

        app.MapGet("/api/print/recovery-context", (FileSessionService sessions) =>
        {
            var jobs = sessions.GetRecoverableJobs();
            var job = jobs.LastOrDefault();
            return Results.Ok(new PrintResponse
            {
                Success = true,
                Message = job == null ? "No recoverable print job." : "Recoverable print job found.",
                JobState = job,
                JobStates = jobs
            });
        });

        app.MapPost("/api/print/continue", async (
            string jobId,
            PrintAlgorithmService printAlgorithm,
            FileSessionService sessions) =>
        {
            // ClaimJob removes the job from memory to prevent concurrent phase-2 execution.
            // If phase 2 fails, put it back so the user can retry, cancel, or recover.
            // The restored job still tracks IntermediateFiles, so cancel/cleanup can delete them later.
            PrintJobState? jobState = null;
            void RestoreClaimedJobForRecovery()
            {
                if (jobState != null)
                    sessions.AddJob(jobState.JobId, jobState);
            }

            try
            {
                // BUG-4 fix: guard null/empty jobId before dictionary lookup
                if (string.IsNullOrWhiteSpace(jobId))
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "jobId is required." });

                // BUG-3 fix: atomically claim the job via TryRemove so concurrent
                // requests for the same jobId cannot both execute phase 2.
                jobState = sessions.ClaimJob(jobId);
                if (jobState == null)
                    return Results.NotFound(new PrintResponse { Success = false, Message = "Job not found or already completed." });

                if (!jobState.WaitingForFlip)
                {
                    RestoreClaimedJobForRecovery();
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "Job is not waiting for flip." });
                }

                // ExecutePrintJob expands manual-duplex copies into a single continuous back pass.
                printAlgorithm.ExecutePrintJob(jobState, firstPhase: false);
                jobState.WaitingForFlip = false;
                jobState.BackPassSent = true;
                sessions.AddJob(jobState.JobId, jobState);

                return Results.Ok(new PrintResponse
                {
                    Success = true,
                    Message = "Back pass sent. Confirm the printed stack or recover damaged sheets.",
                    JobState = jobState
                });
            }
            catch (InvalidOperationException ex)
            {
                RestoreClaimedJobForRecovery();
                return Results.BadRequest(new PrintResponse { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                RestoreClaimedJobForRecovery();
                return Results.Problem($"Error continuing print job: {ex.Message}");
            }
        });

        app.MapPost("/api/print/recover/back/start", (
            Phase2RecoveryRequest request,
            PrintAlgorithmService printAlgorithm,
            FileSessionService sessions) =>
        {
            try
            {
                if (request == null)
                    return Results.BadRequest(new Phase2RecoveryResponse { Success = false, Message = "Request body is required." });
                if (string.IsNullOrWhiteSpace(request.JobId))
                    return Results.BadRequest(new Phase2RecoveryResponse { Success = false, Message = "jobId is required." });
                if (request.SheetIndices == null || request.SheetIndices.Length == 0)
                    return Results.BadRequest(new Phase2RecoveryResponse { Success = false, Message = "Select at least one failed sheet." });

                var job = sessions.GetJob(request.JobId);
                if (job == null)
                    return Results.NotFound(new Phase2RecoveryResponse { Success = false, Message = "Job not found or already completed." });

                var printed = printAlgorithm.StartManualDuplexBackSheetRecovery(job, request.SheetIndices);
                sessions.AddJob(job.JobId, job);
                return Results.Ok(new Phase2RecoveryResponse
                {
                    Success = true,
                    Message = $"Printed {printed} replacement front sheet(s).",
                    PrintedSheets = printed,
                    WaitingForRecoveryFlip = job.WaitingForRecoveryFlip,
                    JobState = job
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new Phase2RecoveryResponse { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error starting back-pass recovery: {ex.Message}");
            }
        });

        app.MapPost("/api/print/recover/back/continue", (
            string jobId,
            PrintAlgorithmService printAlgorithm,
            FileSessionService sessions) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jobId))
                    return Results.BadRequest(new Phase2RecoveryResponse { Success = false, Message = "jobId is required." });

                var job = sessions.GetJob(jobId);
                if (job == null)
                    return Results.NotFound(new Phase2RecoveryResponse { Success = false, Message = "Job not found or already completed." });

                var printed = printAlgorithm.ContinueManualDuplexBackSheetRecovery(job);
                sessions.AddJob(job.JobId, job);
                return Results.Ok(new Phase2RecoveryResponse
                {
                    Success = true,
                    Message = $"Printed {printed} replacement back sheet(s).",
                    PrintedSheets = printed,
                    WaitingForRecoveryFlip = job.WaitingForRecoveryFlip,
                    JobState = job
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new Phase2RecoveryResponse { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error continuing back-pass recovery: {ex.Message}");
            }
        });

        app.MapPost("/api/print/recover/front", (
            Phase1RecoveryRequest request,
            PrintAlgorithmService printAlgorithm,
            FileSessionService sessions) =>
        {
            try
            {
                if (request == null)
                    return Results.BadRequest(new Phase1RecoveryResponse { Success = false, Message = "Request body is required." });
                if (string.IsNullOrWhiteSpace(request.JobId))
                    return Results.BadRequest(new Phase1RecoveryResponse { Success = false, Message = "jobId is required." });
                if (request.SheetIndices == null || request.SheetIndices.Length == 0)
                    return Results.BadRequest(new Phase1RecoveryResponse { Success = false, Message = "Select at least one failed sheet." });

                var job = sessions.GetJob(request.JobId);
                if (job == null)
                    return Results.NotFound(new Phase1RecoveryResponse { Success = false, Message = "Job not found or already completed." });

                var printed = printAlgorithm.ReprintManualDuplexFrontSheets(job, request.SheetIndices);
                sessions.AddJob(job.JobId, job);
                return Results.Ok(new Phase1RecoveryResponse
                {
                    Success = true,
                    Message = $"Reprinted {printed} front sheet(s).",
                    PrintedSheets = printed
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new Phase1RecoveryResponse { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error recovering front sheets: {ex.Message}");
            }
        });

        app.MapPost("/api/print/complete", (string jobId, FileSessionService sessions) =>
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return Results.BadRequest(new PrintResponse { Success = false, Message = "jobId is required." });

            var job = sessions.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new PrintResponse { Success = false, Message = "Job not found or already completed." });

            if (job.IsManualDuplex && job.WaitingForFlip)
                return Results.BadRequest(new PrintResponse { Success = false, Message = "Job is still waiting for flip." });
            if (job.WaitingForRecoveryFlip)
                return Results.BadRequest(new PrintResponse { Success = false, Message = "Recovery job is still waiting for flip." });

            sessions.RemoveJob(jobId);
            return Results.Ok(new PrintResponse { Success = true, Message = "Print job completed successfully" });
        });

        app.MapDelete("/api/print/cancel", (string jobId, FileSessionService sessions) =>
        {
            if (string.IsNullOrWhiteSpace(jobId))
                return Results.BadRequest(new PrintResponse { Success = false, Message = "jobId is required" });

            var job = sessions.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new PrintResponse { Success = false, Message = "Job không tồn tại hoặc đã hoàn tất" });

            sessions.RemoveJob(jobId);
            return Results.Ok(new PrintResponse { Success = true, Message = "Đã hủy lệnh in" });
        });

        app.MapPost("/api/printer/settings", (PrinterSettingsRequest req, PrinterManagementService printerService) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(req.PrinterName))
                    return Results.BadRequest(new { success = false, message = "Printer name is required" });

                var printer = printerService.GetAllPrinters().FirstOrDefault(p =>
                    string.Equals(p.Name, req.PrinterName, StringComparison.OrdinalIgnoreCase));
                if (printer == null)
                    return Results.NotFound(new { success = false, message = "Printer not found" });

                var psi = BuildPrinterSettingsStartInfo(printer.Name);
                Process.Start(psi);

                return Results.Ok(new { success = true, message = "Printer settings dialog opened" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to open printer settings: {ex.Message}");
            }
        });

        return app;
    }

    internal static ProcessStartInfo BuildPrinterSettingsStartInfo(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new ArgumentException("Printer name is required.", nameof(printerName));
        if (printerName.Any(ch => ch == '"' || char.IsControl(ch)))
            throw new ArgumentException("Printer name contains unsupported characters.", nameof(printerName));

        var psi = new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("printui.dll,PrintUIEntry");
        psi.ArgumentList.Add("/e");
        psi.ArgumentList.Add("/n");
        psi.ArgumentList.Add(printerName.Trim());
        return psi;
    }

    private static bool IsAllowedLocalFrontendOrigin(string origin)
    {
        if (string.Equals(origin, "null", StringComparison.Ordinal))
            return true;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        return uri.IsLoopback
            || string.Equals(uri.Host, "app.local", StringComparison.OrdinalIgnoreCase);
    }
}
