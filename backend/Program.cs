using PrinterApp.Models;
using PrinterApp.Services;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

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

// API Endpoints

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
        {
            return Results.BadRequest(new UploadResponse
            {
                Success = false,
                Message = "No file uploaded"
            });
        }

        var file = request.Form.Files[0];
        var extension = Path.GetExtension(file.FileName).ToLower();
        
        Console.WriteLine($"[UPLOAD] Receiving file: {file.FileName}, Size: {file.Length} bytes");
        
        var allowedExtensions = new[] { ".doc", ".docx", ".pdf", ".jpg", ".jpeg", ".png" };
        if (!allowedExtensions.Contains(extension))
        {
            return Results.BadRequest(new UploadResponse
            {
                Success = false,
                Message = $"File type not supported. Allowed: {string.Join(", ", allowedExtensions)}"
            });
        }

        var fileId = Guid.NewGuid().ToString();
        var tempPath = Path.Combine(Path.GetTempPath(), $"{fileId}{extension}");

        Console.WriteLine($"[UPLOAD] Saving to: {tempPath}");

        using (var stream = new FileStream(tempPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Verify file was saved correctly
        var fileInfo = new FileInfo(tempPath);
        Console.WriteLine($"[UPLOAD] File saved successfully. Size on disk: {fileInfo.Length} bytes");

        if (fileInfo.Length == 0)
        {
            Console.WriteLine($"[UPLOAD ERROR] File is empty!");
            return Results.BadRequest(new UploadResponse
            {
                Success = false,
                Message = "Uploaded file is empty"
            });
        }

        sessions.AddFile(fileId, tempPath);

        Console.WriteLine($"[UPLOAD] Upload complete. FileId: {fileId}");

        return Results.Ok(new UploadResponse
        {
            Success = true,
            FileId = fileId,
            OriginalFileName = file.FileName
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[UPLOAD ERROR] {ex.Message}");
        return Results.Problem($"Error uploading file: {ex.Message}");
    }
});

app.MapGet("/api/file/{fileId}", (string fileId, FileSessionService sessions) =>
{
    try
    {
        Console.WriteLine($"[FILE] Request for fileId: {fileId}");
        
        var filePath = sessions.GetFilePath(fileId);
        if (filePath == null)
        {
            Console.WriteLine($"[FILE ERROR] FileId not found in session: {fileId}");
            return Results.NotFound("File not found");
        }

        Console.WriteLine($"[FILE] File path from cache: {filePath}");

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[FILE ERROR] File does not exist on disk: {filePath}");
            return Results.NotFound("File not found on disk");
        }

        var fileInfo = new FileInfo(filePath);
        Console.WriteLine($"[FILE] Serving file: {filePath}, Size: {fileInfo.Length} bytes");
        
        var fileBytes = File.ReadAllBytes(filePath);
        return Results.File(fileBytes, "application/pdf");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FILE ERROR] {ex.Message}");
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
        {
            // Already PDF
            return Results.Ok(new { pdfPath });
        }
        else if (extension is ".doc" or ".docx")
        {
            wordService.ConvertToPdf(filePath, pdfPath);
        }
        else if (extension is ".jpg" or ".jpeg" or ".png")
        {
            wordService.ConvertImageToPdf(filePath, pdfPath);
        }
        else
        {
            return Results.BadRequest($"Unsupported file type for conversion: {extension}");
        }

        // Update the file path to PDF
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
        Console.WriteLine($"[PRINT] Received print request for fileId: {request.FileId}, printer: {request.PrinterName}, mode: {request.Mode}");
        
        var filePath = sessions.GetFilePath(request.FileId);
        if (filePath == null)
        {
            Console.WriteLine($"[PRINT ERROR] File not found for fileId: {request.FileId}");
            return Results.NotFound(new PrintResponse
            {
                Success = false,
                Message = "File not found. Please upload the file again."
            });
        }

        Console.WriteLine($"[PRINT] File path: {filePath}");

        // Ensure file is PDF
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension != ".pdf")
        {
            Console.WriteLine($"[PRINT ERROR] File is not PDF: {extension}");
            return Results.BadRequest(new PrintResponse
            {
                Success = false,
                Message = "File must be converted to PDF first. Call /api/convert"
            });
        }

        // Check if file exists
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[PRINT ERROR] File does not exist on disk: {filePath}");
            return Results.NotFound(new PrintResponse
            {
                Success = false,
                Message = "File not found on server. Please upload again."
            });
        }

        // Check printer availability
        Console.WriteLine($"[PRINT] Checking printer availability...");
        if (!printerService.IsPrinterAvailable(request.PrinterName))
        {
            Console.WriteLine($"[PRINT ERROR] Printer not available: {request.PrinterName}");
            return Results.BadRequest(new PrintResponse
            {
                Success = false,
                Message = "Printer is not available"
            });
        }

        // Determine if printer supports duplex
        Console.WriteLine($"[PRINT] Getting printer info...");
        var printers = printerService.GetAllPrinters();
        var printer = printers.FirstOrDefault(p => p.Name == request.PrinterName);
        
        if (printer == null)
        {
            Console.WriteLine($"[PRINT ERROR] Printer not found in list: {request.PrinterName}");
            return Results.NotFound(new PrintResponse
            {
                Success = false,
                Message = "Printer not found"
            });
        }

        Console.WriteLine($"[PRINT] Printer duplex support: {printer.IsDuplex}");
        Console.WriteLine($"[PRINT] Page range: {request.PageRange ?? "all pages"}");
        Console.WriteLine($"[PRINT] Single-sided pages: {(request.SingleSidedPages != null ? string.Join(",", request.SingleSidedPages) : "none")}");

        PrintJobState jobState;

        if (request.Mode == PrintMode.NormalDuplex)
        {
            Console.WriteLine($"[PRINT] Creating normal duplex job...");
            jobState = printAlgorithm.CreateNormalDuplexJob(
                filePath,
                request.PrinterName,
                printer.IsDuplex,
                request.PageRange,
                request.SingleSidedPages,
                request.Watermark
            );
        }
        else if (request.Mode == PrintMode.Simplex)
        {
            Console.WriteLine($"[PRINT] Creating simplex job...");
            jobState = printAlgorithm.CreateSimplexJob(
                filePath,
                request.PrinterName,
                request.PageRange,
                request.Watermark
            );
        }
        else // BookletA5
        {
            Console.WriteLine($"[PRINT] Creating booklet job...");
            jobState = printAlgorithm.CreateBookletJob(
                filePath,
                request.PrinterName,
                printer.IsDuplex,
                request.PageRange,
                request.SingleSidedPages
            );
        }

        // Execute first phase
        int copies = Math.Max(1, request.Copies);
        Console.WriteLine($"[PRINT] Executing print job, manual duplex: {jobState.IsManualDuplex}, copies: {copies}");

        if (!jobState.IsManualDuplex)
        {
            // Auto duplex / simplex: print N copies directly
            for (int copy = 0; copy < copies; copy++)
            {
                Console.WriteLine($"[PRINT] Printing copy {copy + 1}/{copies}");
                printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);
                if (copies > 1 && copy < copies - 1)
                    System.Threading.Thread.Sleep(2000);
            }
        }
        else
        {
            // Manual duplex: phase1 only here, store copies count
            printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);
            jobState.Copies = copies;
        }

        // Store job state for potential continuation
        sessions.AddJob(jobState.JobId, jobState);

        Console.WriteLine($"[PRINT] Print job created successfully. JobId: {jobState.JobId}, WaitingForFlip: {jobState.WaitingForFlip}");

        return Results.Ok(new PrintResponse
        {
            Success = true,
            Message = jobState.WaitingForFlip 
                ? "First phase complete. Waiting for manual flip."
                : "Print job sent successfully.",
            JobState = jobState.WaitingForFlip ? jobState : null
        });
    }
    catch (Exception ex)
    {
        var logMsg = $"[PRINT EXCEPTION] {ex.GetType().Name}: {ex.Message}\nStack trace: {ex.StackTrace}";
        if (ex.InnerException != null)
        {
            logMsg += $"\nInner exception: {ex.InnerException.Message}";
        }
        Console.WriteLine(logMsg);
        try { File.AppendAllText("backend_error.log", logMsg + "\n"); } catch { }
        
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
        var jobState = sessions.GetJob(jobId);
        if (jobState == null)
            return Results.NotFound(new PrintResponse { Success = false, Message = "Job not found" });

        if (!jobState.WaitingForFlip)
        {
            return Results.BadRequest(new PrintResponse
            {
                Success = false,
                Message = "Job is not waiting for flip"
            });
        }

        // Execute second phase (even pages)
        printAlgorithm.ExecutePrintJob(jobState, firstPhase: false);

        jobState.WaitingForFlip = false;
        sessions.RemoveJob(jobId);
        
        return Results.Ok(new PrintResponse
        {
            Success = true,
            Message = "Print job completed successfully"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error continuing print job: {ex.Message}");
    }
});

// Cancel a pending print job (A)
app.MapDelete("/api/print/cancel", (
    string jobId,
    FileSessionService sessions) =>
{
    if (string.IsNullOrWhiteSpace(jobId))
        return Results.BadRequest(new PrintResponse { Success = false, Message = "jobId is required" });

    var job = sessions.GetJob(jobId);
    if (job == null)
        return Results.NotFound(new PrintResponse { Success = false, Message = "Job không tồn tại hoặc đã hoàn tất" });

    sessions.RemoveJob(jobId);
    Console.WriteLine($"[PRINT] Job cancelled: {jobId}");

    return Results.Ok(new PrintResponse { Success = true, Message = "Đã hủy lệnh in" });
});

Console.WriteLine("🖨️  Printer App Backend running on http://localhost:8787");
Console.WriteLine("📄 Endpoints:");
Console.WriteLine("   GET  /api/printers");
Console.WriteLine("   POST /api/upload");
Console.WriteLine("   POST /api/convert");
Console.WriteLine("   POST /api/print");
Console.WriteLine("   POST /api/print/continue");
Console.WriteLine("   DEL  /api/print/cancel");

app.Run("http://localhost:8787");
