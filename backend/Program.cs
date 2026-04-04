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
builder.Services.AddSingleton<WordInteropService>();
builder.Services.AddSingleton<PrintAlgorithmService>();

var app = builder.Build();

app.UseCors("LocalWebApp");

// In-memory storage for uploaded files and jobs
var uploadedFiles = new Dictionary<string, string>();
var printJobs = new Dictionary<string, PrintJobState>();

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

app.MapPost("/api/upload", async (HttpRequest request, IWebHostEnvironment env) =>
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

        uploadedFiles[fileId] = tempPath;

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

app.MapGet("/api/file/{fileId}", (string fileId) =>
{
    try
    {
        Console.WriteLine($"[FILE] Request for fileId: {fileId}");
        Console.WriteLine($"[FILE] Total files in cache: {uploadedFiles.Count}");
        
        if (!uploadedFiles.TryGetValue(fileId, out var filePath))
        {
            Console.WriteLine($"[FILE ERROR] FileId not found in cache");
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
    WordInteropService wordService) =>
{
    try
    {
        if (!uploadedFiles.TryGetValue(fileId, out var filePath))
        {
            return Results.NotFound("File not found");
        }

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
        else
        {
            return Results.BadRequest("Image conversion is not currently supported. Please upload PDF or Word documents.");
        }

        // Update the file path to PDF
        uploadedFiles[fileId] = pdfPath;

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
    WordInteropService wordService,
    PrintAlgorithmService printAlgorithm) =>
{
    try
    {
        Console.WriteLine($"[PRINT] Received print request for fileId: {request.FileId}, printer: {request.PrinterName}, mode: {request.Mode}");
        
        if (!uploadedFiles.TryGetValue(request.FileId, out var filePath))
        {
            Console.WriteLine($"[PRINT ERROR] File not found for fileId: {request.FileId}");
            Console.WriteLine($"[PRINT ERROR] Available fileIds: {string.Join(", ", uploadedFiles.Keys)}");
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
                request.SingleSidedPages
            );
        }
        else // BookletA5
        {
            Console.WriteLine($"[PRINT] Creating booklet job...");
            jobState = printAlgorithm.CreateBookletJob(
                filePath,
                request.PrinterName,
                printer.IsDuplex,
                request.PageRange
            );
        }

        // Execute first phase
        Console.WriteLine($"[PRINT] Executing print job, manual duplex: {jobState.IsManualDuplex}...");
        printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);

        // Store job state for potential continuation
        printJobs[jobState.JobId] = jobState;

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
    PrintAlgorithmService printAlgorithm) =>
{
    try
    {
        if (!printJobs.TryGetValue(jobId, out var jobState))
        {
            return Results.NotFound(new PrintResponse
            {
                Success = false,
                Message = "Job not found"
            });
        }

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

Console.WriteLine("🖨️  Printer App Backend running on http://localhost:8787");
Console.WriteLine("📄 Endpoints:");
Console.WriteLine("   GET  /api/printers");
Console.WriteLine("   POST /api/upload");
Console.WriteLine("   POST /api/convert");
Console.WriteLine("   POST /api/print");
Console.WriteLine("   POST /api/print/continue");

app.Run("http://localhost:8787");
