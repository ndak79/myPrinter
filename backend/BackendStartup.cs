using PrinterApp.Models;
using PrinterApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;

namespace PrinterApp;

/// <summary>
/// Centralised backend startup — can be called both from the standalone
/// CLI entry point (Program.cs) and from the WinForms desktop host.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BackendStartup
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

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
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // Configure file upload limits
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 104857600; // 100 MB
        });

        // Register services
        builder.Services.AddSingleton<PrinterManagementService>();
        builder.Services.AddSingleton<IWordInteropService, WordInteropService>();
        builder.Services.AddSingleton<PrintAlgorithmService>();
        builder.Services.AddSingleton<FileSessionService>();

        var app = builder.Build();

        app.UseCors("LocalWebApp");

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
                return Results.Ok(new { success = true, pdfPath });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error converting file: {ex.Message}");
            }
        });

        app.MapPost("/api/print", (
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
                if (!printerService.IsPrinterAvailable(request.PrinterName))
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "Printer is not available." });

                PrintJobState jobState;

                // BUG-2 fix: explicit else-if for each mode + reject unknown values
                if (request.Mode == PrintMode.NormalDuplex)
                    jobState = printAlgorithm.CreateNormalDuplexJob(filePath, request.PrinterName, printer.IsDuplex,
                        request.PageRange, request.SingleSidedPages, request.Watermark, request.PageOrder, request.PageRotations,
                        duplexSide:    request.DuplexSide,
                        manualFlipDir: request.ManualFlipDir);
                else if (request.Mode == PrintMode.Simplex)
                    jobState = printAlgorithm.CreateSimplexJob(filePath, request.PrinterName,
                        request.PageRange, request.Watermark, request.PageOrder, request.PageRotations);
                else if (request.Mode == PrintMode.BookletA5)
                    jobState = printAlgorithm.CreateBookletJob(filePath, request.PrinterName, printer.IsDuplex,
                        request.PageRange, request.SingleSidedPages, request.PageOrder, request.PageRotations);
                else
                    return Results.BadRequest(new PrintResponse { Success = false, Message = $"Unknown print mode: {(int)request.Mode}" });

                int copies = Math.Max(1, request.Copies);

                // BUG-6 fix: only store the job in session if it's a manual duplex waiting for flip.
                // Completed non-manual jobs don't need to be stored and would accumulate in memory.
                if (!jobState.IsManualDuplex)
                {
                    for (int copy = 0; copy < copies; copy++)
                    {
                        printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);
                        if (copies > 1 && copy < copies - 1)
                            System.Threading.Thread.Sleep(2000);
                    }
                    // No AddJob — print is complete, nothing to continue
                }
                else
                {
                    printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);
                    jobState.Copies = copies;
                    sessions.AddJob(jobState.JobId, jobState);
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

        app.MapPost("/api/print/continue", (
            string jobId,
            PrintAlgorithmService printAlgorithm,
            FileSessionService sessions) =>
        {
            try
            {
                // BUG-4 fix: guard null/empty jobId before dictionary lookup
                if (string.IsNullOrWhiteSpace(jobId))
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "jobId is required." });

                // BUG-3 fix: atomically claim the job via TryRemove so concurrent
                // requests for the same jobId cannot both execute phase 2.
                var jobState = sessions.ClaimJob(jobId);
                if (jobState == null)
                    return Results.NotFound(new PrintResponse { Success = false, Message = "Job not found or already completed." });

                if (!jobState.WaitingForFlip)
                    return Results.BadRequest(new PrintResponse { Success = false, Message = "Job is not waiting for flip." });

                printAlgorithm.ExecutePrintJob(jobState, firstPhase: false);
                jobState.WaitingForFlip = false;

                return Results.Ok(new PrintResponse { Success = true, Message = "Print job completed successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new PrintResponse { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error continuing print job: {ex.Message}");
            }
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

        return app;
    }
}
