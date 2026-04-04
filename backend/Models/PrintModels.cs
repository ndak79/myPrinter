using System;
using System.Collections.Generic;
using System.Linq;

namespace PrinterApp.Models
{
    public class PrinterInfo
    {
        public string Name { get; set; } = "";
        public bool IsDefault { get; set; }
        public bool IsDuplex { get; set; }
        public PrinterStatus Status { get; set; }
    }

    public enum PrinterStatus
    {
        Unknown = 0,
        Other = 1,
        Ready = 3,
        Printing = 4,
        Warmup = 5,
        Offline = 7
    }

    public class PrintRequest
    {
        public string FileId { get; set; } = "";
        public string PrinterName { get; set; } = "";
        public PrintMode Mode { get; set; }
        public string? PageRange { get; set; } // e.g. "1-3,5,7-9" or null for all pages
        public int[]? SingleSidedPages { get; set; } // Pages that should be printed single-sided
    }

    public enum PrintMode
    {
        NormalDuplex,
        BookletA5
    }

    public class PrintJobState
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// True nếu đang in manual duplex (máy in 1 mặt, nhưng mình in 2 lượt).
        /// </summary>
        public bool IsManualDuplex { get; set; }

        /// <summary>
        /// Sau phase 1, đang chờ người dùng lật giấy.
        /// </summary>
        public bool WaitingForFlip { get; set; }

        public FlipInstruction? Instruction { get; set; }

        /// <summary>
        /// Các processed page index dùng cho lần in 1 (front pass).
        /// (Giữ tên cũ OddPages để không vỡ API, nhưng thực chất là Phase1Pages từ ManualDuplexPlan)
        /// </summary>
        public int[] OddPages { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Các processed page index dùng cho lần in 2 (back pass).
        /// (Giữ tên cũ RemainingPages, thực chất là Phase2Pages từ ManualDuplexPlan)
        /// </summary>
        public int[] RemainingPages { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Đường dẫn tới PDF đã xử lý (subset + mixed-orientation + padding).
        /// </summary>
        public string TempPdfPath { get; set; } = "";

        public string PrinterName { get; set; } = "";

        /// <summary>
        /// Kế hoạch manual duplex đầy đủ (mapping sheet / front / back / phase1 / phase2).
        /// Không bắt buộc, nhưng rất hữu ích cho debug & UI.
        /// </summary>
        public ManualDuplexPlan? ManualPlan { get; set; }
    }

    public class FlipInstruction
    {
        public FlipDirection Direction { get; set; }
        public string Text { get; set; } = "";
        public string VisualType { get; set; } = "";
    }

    public enum FlipDirection
    {
        LongEdge,
        ShortEdge
    }

    public class PrintResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public PrintJobState? JobState { get; set; }
    }

    public class UploadResponse
    {
        public bool Success { get; set; }
        public string? FileId { get; set; }
        public string? OriginalFileName { get; set; }
        public string? Message { get; set; }
    }

    // ===============================================================
    //  MANUAL DUPLEX PLAN MODELS
    // ===============================================================

    /// <summary>
    /// Thông tin 1 trang trong file PDF đã xử lý (sau mixed-orientation + padding).
    /// ProcessedIndex: vị trí trong file processed (1-based).
    /// OriginalPageNumber: trang gốc trong subset (1-based), -1 nếu là blank thêm vào.
    /// </summary>
    public sealed class ManualDuplexPageInfo
    {
        public int ProcessedIndex { get; set; }          // 1..N trong file processed
        public int OriginalPageNumber { get; set; }      // 1..M trong subset gốc, -1 nếu BLANK
        public bool IsBlank { get; set; }
        public bool IsLandscape { get; set; }

        public override string ToString()
        {
            var orig = IsBlank ? "BLANK" : OriginalPageNumber.ToString();
            var ori = IsLandscape ? "L" : "P";
            return $"#{ProcessedIndex}: orig={orig}, {ori}, blank={IsBlank}";
        }
    }

    /// <summary>
    /// 1 tờ giấy vật lý trong manual duplex: Front/Back trỏ tới các processed page.
    /// </summary>
    public sealed class ManualDuplexSheet
    {
        public int SheetIndex { get; set; }              // 1..S
        public ManualDuplexPageInfo Front { get; set; } = default!;
        public ManualDuplexPageInfo Back  { get; set; } = default!;
    }

    /// <summary>
    /// Kế hoạch in manual duplex hoàn chỉnh:
    /// - ProcessedPdfPath: file PDF sau khi ProcessMixedOrientation (có padding/blank).
    /// - ProcessedPages: danh sách page info (gồm cả blank).
    /// - Sheets: mapping front/back theo từng tờ.
    /// - Phase1Pages: các processedIndex dùng cho phase 1 (front pass).
    /// - Phase2Pages: các processedIndex dùng cho phase 2 (back pass, đã reorder theo kiểu face-down).
    /// </summary>
    public sealed class ManualDuplexPlan
    {
        public string ProcessedPdfPath { get; init; } = string.Empty;
        public IReadOnlyList<ManualDuplexPageInfo> ProcessedPages { get; init; } = Array.Empty<ManualDuplexPageInfo>();
        public IReadOnlyList<ManualDuplexSheet> Sheets { get; init; } = Array.Empty<ManualDuplexSheet>();
        public int[] Phase1Pages { get; init; } = Array.Empty<int>();
        public int[] Phase2Pages { get; init; } = Array.Empty<int>();

        /// <summary>
        /// Xây plan từ list ManualDuplexPageInfo (sau ProcessMixedOrientation).
        /// faceDownStack = true cho máy in như Canon LBP2900 (trang in sau nằm trên cùng xấp giấy).
        /// </summary>
        public static ManualDuplexPlan Build(string processedPdfPath,
                                             IList<ManualDuplexPageInfo> processedPages,
                                             bool faceDownStack = true)
        {
            if (processedPages == null) throw new ArgumentNullException(nameof(processedPages));
            if (processedPages.Count == 0) throw new ArgumentException("Processed pages list is empty.", nameof(processedPages));

            // Đảm bảo ProcessedIndex = 1..N
            for (int i = 0; i < processedPages.Count; i++)
            {
                processedPages[i].ProcessedIndex = i + 1;
            }

            // Sheet = (1,2), (3,4), ...
            var sheets = new List<ManualDuplexSheet>();
            for (int i = 0; i < processedPages.Count; i += 2)
            {
                var front = processedPages[i];
                ManualDuplexPageInfo back;

                if (i + 1 < processedPages.Count)
                {
                    back = processedPages[i + 1];
                }
                else
                {
                    // Phòng hờ, thường không xảy ra vì đã padding chẵn
                    back = new ManualDuplexPageInfo
                    {
                        ProcessedIndex = -1,
                        OriginalPageNumber = -1,
                        IsBlank = true,
                        IsLandscape = front.IsLandscape
                    };
                }

                sheets.Add(new ManualDuplexSheet
                {
                    SheetIndex = (i / 2) + 1,
                    Front = front,
                    Back = back
                });
            }

            // Phase 1: in các trang lẻ (front sides)
            var phase1 = processedPages
                .Where(p => p.ProcessedIndex % 2 == 1)
                .Select(p => p.ProcessedIndex)
                .ToArray();

            // Phase 2: in các trang chẵn (back sides)
            var even = processedPages
                .Where(p => p.ProcessedIndex % 2 == 0)
                .Select(p => p.ProcessedIndex)
                .ToArray();

            int[] phase2 = faceDownStack
                ? even.Reverse().ToArray()
                : even;

            return new ManualDuplexPlan
            {
                ProcessedPdfPath = processedPdfPath,
                ProcessedPages = processedPages.ToArray(),
                Sheets = sheets,
                Phase1Pages = phase1,
                Phase2Pages = phase2
            };
        }

        /// <summary>
        /// Log debug để soi nhanh mapping sheet / phase1 / phase2.
        /// </summary>
        public void DumpToConsole()
        {
            Console.WriteLine("============== MANUAL DUPLEX PLAN ==============");
            Console.WriteLine($"Processed PDF : {ProcessedPdfPath}");
            Console.WriteLine($"Total pages   : {ProcessedPages.Count}");
            Console.WriteLine($"Total sheets  : {Sheets.Count}");
            Console.WriteLine();
            Console.WriteLine("Processed pages (index -> orig / orientation / blank):");
            foreach (var p in ProcessedPages)
            {
                Console.WriteLine($"  {p}");
            }

            Console.WriteLine();
            Console.WriteLine("Sheets mapping:");
            foreach (var s in Sheets)
            {
                var frontOrig = s.Front.IsBlank ? "BLANK" : s.Front.OriginalPageNumber.ToString();
                var backOrig  = s.Back.IsBlank  ? "BLANK" : s.Back.OriginalPageNumber.ToString();
                Console.WriteLine(
                    $"  Sheet {s.SheetIndex}: " +
                    $"Front=proc {s.Front.ProcessedIndex} (orig {frontOrig}), " +
                    $"Back=proc {s.Back.ProcessedIndex} (orig {backOrig})");
            }

            Console.WriteLine();
            Console.WriteLine($"Phase 1 pages (front pass): {string.Join(", ", Phase1Pages)}");
            Console.WriteLine($"Phase 2 pages (back  pass): {string.Join(", ", Phase2Pages)}");
            Console.WriteLine("=================================================");
        }
    }

    /// <summary>
    /// Đại diện cho 1 file đã upload + thời điểm tạo (dùng cho TTL cleanup).
    /// </summary>
    public sealed class FileSession
    {
        public string FileId    { get; init; } = "";
        public string FilePath  { get; set; }  = "";
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }
}
