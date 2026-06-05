using System.Diagnostics;
using System.Text.Json;

namespace PrinterApp.Services.WordConversion;

internal interface IWordConversionWorkerClient
{
    WordConversionWorkerResult Convert(string inputPath, string outputPath, TimeSpan timeout);
}

internal sealed class WordConversionWorkerClient : IWordConversionWorkerClient
{
    public WordConversionWorkerResult Convert(string inputPath, string outputPath, TimeSpan timeout)
    {
        var resultPath = Path.Combine(Path.GetTempPath(), $"myprinter-word-convert-{Guid.NewGuid():N}.json");

        try
        {
            using var process = Process.Start(BuildStartInfo(inputPath, outputPath, resultPath))
                ?? throw new InvalidOperationException("Could not start Word conversion worker process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeout))
            {
                TryKill(process);
                throw new TimeoutException($"Word conversion timed out after {timeout.TotalSeconds:F0} seconds.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            if (!File.Exists(resultPath))
            {
                throw new InvalidOperationException(
                    $"Word conversion worker exited without a result file. ExitCode={process.ExitCode}. {JoinOutput(stdout, stderr)}");
            }

            var result = JsonSerializer.Deserialize<WordConversionWorkerResult>(File.ReadAllText(resultPath))
                ?? throw new InvalidOperationException("Word conversion worker returned an unreadable result.");

            if (process.ExitCode != 0 || !result.Success)
            {
                var detail = result.Error ?? JoinOutput(stdout, stderr);
                throw new InvalidOperationException($"Word conversion worker failed. {detail}".Trim());
            }

            return result;
        }
        finally
        {
            try { if (File.Exists(resultPath)) File.Delete(resultPath); } catch { }
        }
    }

    internal static ProcessStartInfo BuildStartInfo(string inputPath, string outputPath, string resultPath)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("Cannot locate the current application executable for Word conversion.");

        var entryAssemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        var entryAssembly = string.IsNullOrWhiteSpace(entryAssemblyName)
            ? null
            : Path.Combine(AppContext.BaseDirectory, $"{entryAssemblyName}.dll");
        var isDotnetHost = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        if (isDotnetHost && (string.IsNullOrWhiteSpace(entryAssembly) || !File.Exists(entryAssembly)))
            throw new InvalidOperationException("Cannot locate the current application assembly for Word conversion.");

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (isDotnetHost)
            startInfo.ArgumentList.Add(entryAssembly!);

        startInfo.ArgumentList.Add(WordConversionWorkerCommand.CommandSwitch);
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--result");
        startInfo.ArgumentList.Add(resultPath);

        return startInfo;
    }

    private static string JoinOutput(string stdout, string stderr)
    {
        return string.Join(" ", new[] { stdout.Trim(), stderr.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }
}
