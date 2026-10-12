// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Nhật ký CHI TIẾT của các lệnh xuất DXF (Flatten→DXF, View2DXF). Bản sao của
    /// ITool src\ITool.SwCore\DXF\DxfTrace.cs — sửa thì sửa cả hai. Mỗi lượt bấm là MỘT file riêng
    /// <c>%AppData%\XToolPro\logs\dxf\dxf-&lt;lệnh&gt;-yyyyMMdd-HHmmss.log</c> để người thử
    /// trên máy có SolidWorks gửi nguyên file về cho người phân tích (máy phát triển
    /// không có SolidWorks): đầu file là môi trường (phiên bản module, Windows, .NET,
    /// SolidWorks, cài đặt DXF toàn cục), sau đó từng chi tiết → từng bước kèm thời
    /// gian, mã lỗi COM/SaveAs, thống kê layer của DXF thô và DXF cuối.
    ///
    /// Trước đây phần lớn lỗi dọc đường đi vào <c>Debug.WriteLine</c> — bị bỏ hẳn ở bản
    /// Release — hoặc <c>catch { }</c>, nên log người dùng gửi về chỉ có câu cuối cùng.
    /// Thuần .NET (không SolidWorks) để test và bộ thử link được.
    /// </summary>
    internal static class DxfTrace
    {
        /// <summary>Giữ tối đa chừng này file phiên; file cũ hơn bị xoá khi mở phiên mới.</summary>
        private const int KeepSessionFiles = 60;

        /// <summary>Quá cỡ này thì bỏ dòng DEBUG (vẫn ghi INFO/WARN/ERROR) để file gửi được.</summary>
        private const long MaxDebugBytes = 24L * 1024 * 1024;

        private static readonly object Gate = new object();
        private static StreamWriter _writer;
        private static string _sessionPath;
        private static Stopwatch _sessionClock;
        private static long _lastMark;
        private static int _indent;
        private static int _errors, _warnings;
        private static bool _debugCapped;

        /// <summary>Thư mục chứa mọi file nhật ký DXF.</summary>
        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TAI_TOOL", "logs", "dxf-export");

        /// <summary>File của phiên đang mở (null khi không có phiên).</summary>
        public static string CurrentPath { get { lock (Gate) return _sessionPath; } }

        /// <summary>File của phiên gần nhất (kể cả đã đóng) — để hộp thoại kết quả trỏ tới.</summary>
        public static string LastPath { get; private set; }

        public static bool IsActive { get { lock (Gate) return _writer != null; } }

        /// <summary>
        /// Mở phiên cho một lượt xuất. <paramref name="feature"/> là tên ngắn trong tên
        /// file ("bom", "view2dxf"). Phiên lồng nhau dùng lại phiên ngoài.
        /// </summary>
        public static IDisposable BeginSession(string feature, string title)
        {
            lock (Gate)
            {
                if (_writer != null)
                {
                    WriteLocked("INFO", "── phiên lồng: " + title);
                    return new NoopScope();
                }
                try
                {
                    Directory.CreateDirectory(Folder);
                    Prune();
                    string safe = string.IsNullOrWhiteSpace(feature) ? "dxf" : Sanitize(feature);
                    string path = Path.Combine(Folder,
                        "dxf-" + safe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".log");
                    _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true))
                    {
                        AutoFlush = true   // SolidWorks treo/chết giữa chừng vẫn còn log tới dòng cuối
                    };
                    _sessionPath = path;
                    LastPath = path;
                    _sessionClock = Stopwatch.StartNew();
                    _lastMark = 0;
                    _indent = 0;
                    _errors = 0;
                    _warnings = 0;
                    _debugCapped = false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("DxfTrace: cannot open session file: " + ex.Message);
                    _writer = null;
                    _sessionPath = null;
                    return new NoopScope();
                }
                WriteHeaderLocked(feature, title);
            }
            return new SessionScope();
        }

        private static void EndSession()
        {
            lock (Gate)
            {
                if (_writer == null) return;
                _indent = 0;
                WriteLocked("INFO", "══ KẾT THÚC phiên · " + Seconds(_sessionClock.ElapsedMilliseconds) +
                                    " · " + _errors + " lỗi · " + _warnings + " cảnh báo");
                try { _writer.Dispose(); } catch { }
                _writer = null;
                _sessionPath = null;
                _sessionClock = null;
            }
        }

        // ---------------- ghi dòng ----------------

        public static void Info(string text) => Write("INFO", text);

        public static void Debug(string text)
        {
            System.Diagnostics.Debug.WriteLine(text);
            Write("DEBUG", text);
        }

        public static void Warn(string text) => Write("WARN", text);

        public static void Error(string text, Exception ex = null) =>
            Write("ERROR", ex == null ? text : text + Environment.NewLine + Describe(ex));

        /// <summary>Một dòng «khoá = giá trị» (tham số, cài đặt).</summary>
        public static void Value(string key, object value) =>
            Write("INFO", "  " + key + " = " + FormatValue(value));

        /// <summary>
        /// Mốc bước: ghi tên bước kèm thời gian đã trôi qua từ mốc trước
        /// (là thời gian của bước TRƯỚC). Lỗi của bước được nơi bắt ghi bằng <see cref="Error"/>.
        /// </summary>
        public static void Phase(string name)
        {
            lock (Gate)
            {
                if (_writer == null) return;
                long now = _sessionClock.ElapsedMilliseconds;
                WriteLocked("STEP", "▶ " + name + "   (bước trước " + Seconds(now - _lastMark) + ")");
                _lastMark = now;
            }
        }

        /// <summary>Thụt lề một khối (một chi tiết, một view); Dispose ghi tổng thời gian khối.</summary>
        public static IDisposable Block(string title)
        {
            lock (Gate)
            {
                if (_writer == null) return new NoopScope();
                WriteLocked("INFO", "┌─ " + title);
                _indent++;
                _lastMark = _sessionClock.ElapsedMilliseconds;
                return new BlockScope(title, _sessionClock.ElapsedMilliseconds);
            }
        }

        /// <summary>Thông báo đi vào báo cáo chi tiết (DxfExportNotices).</summary>
        public static void Notice(string level, string category, string text) =>
            Write(string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase) ? "ERROR"
                : string.Equals(level, "Warning", StringComparison.OrdinalIgnoreCase) ? "WARN" : "NOTE",
                "[" + category + "] " + text);

        /// <summary>
        /// Chép dòng của ErrorEvents (error log / RenkeiDebug.log) vào phiên đang mở để
        /// mọi thứ của một lượt xuất nằm trong một file. Nơi mở phiên gắn hàm này vào
        /// <c>ErrorEvents.Mirror</c> (DxfTrace không tham chiếu ErrorEvents để test link được).
        /// </summary>
        public static void FromErrorEvents(string kind, string message) =>
            Write(kind == "ERROR" ? "ERRLOG" : "DBGLOG", message);

        private static void Write(string level, string text)
        {
            lock (Gate)
            {
                if (_writer == null)
                {
                    // Ngoài phiên (bộ thử, module khác dùng chung mã): chỉ giữ lỗi/cảnh báo.
                    if (level == "ERROR" || level == "WARN") WriteAdHoc(level, text);
                    return;
                }
                WriteLocked(level, text);
            }
        }

        private static void WriteLocked(string level, string text)
        {
            if (level == "ERROR" || level == "ERRLOG") _errors++;
            else if (level == "WARN") _warnings++;
            try
            {
                if (level == "DEBUG" || level == "DBGLOG")
                {
                    if (_debugCapped) return;
                    if (_writer.BaseStream.Length > MaxDebugBytes)
                    {
                        _debugCapped = true;
                        text = "(file log quá " + (MaxDebugBytes / 1024 / 1024) + " MB — từ đây bỏ dòng DEBUG)";
                        level = "WARN";
                    }
                }
                string stamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                string pad = new string(' ', Math.Max(0, _indent) * 2);
                string body = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\n" + new string(' ', 22) + pad);
                _writer.WriteLine(stamp + " " + level.PadRight(6) + " " + pad + body);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DxfTrace write failed: " + ex.Message);
            }
        }

        private static void WriteAdHoc(string level, string text)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, "dxf-adhoc-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + level.PadRight(6) + " " + text + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        private static void WriteHeaderLocked(string feature, string title)
        {
            WriteLocked("INFO", "══ XToolPro DXF trace · " + feature + " · " + title);
            WriteLocked("INFO", "  thời điểm = " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            WriteLocked("INFO", "  file log = " + _sessionPath);
            try
            {
                Assembly own = typeof(DxfTrace).Assembly;
                AssemblyName name = own.GetName();
                string info = own.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .OfType<AssemblyInformationalVersionAttribute>().Select(a => a.InformationalVersion).FirstOrDefault();
                string fileVersion = own.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)
                    .OfType<AssemblyFileVersionAttribute>().Select(a => a.Version).FirstOrDefault();
                WriteLocked("INFO", "  module = " + name.Name + " " + name.Version +
                                    (string.IsNullOrEmpty(fileVersion) ? "" : " · file " + fileVersion) +
                                    (string.IsNullOrEmpty(info) ? "" : " · " + info));
                string location = "";
                try { location = own.Location; } catch { }
                WriteLocked("INFO", "  module nạp từ = " + (string.IsNullOrEmpty(location) ? "(bộ nhớ — Assembly.Load(byte[]))" : location));
            }
            catch (Exception ex) { WriteLocked("WARN", "  không đọc được phiên bản module: " + ex.Message); }
            WriteLocked("INFO", "  Windows = " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " x64" : " x86") +
                                " · tiến trình " + (Environment.Is64BitProcess ? "64" : "32") + "-bit · CLR " + Environment.Version);
            WriteLocked("INFO", "  culture = " + CultureInfo.CurrentCulture.Name + " / UI " + CultureInfo.CurrentUICulture.Name +
                                " · dấu thập phân '" + CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator + "'" +
                                " · ANSI codepage " + Encoding.Default.CodePage);
            try
            {
                Process self = Process.GetCurrentProcess();
                WriteLocked("INFO", "  tiến trình = " + self.ProcessName + " pid " + self.Id +
                                    " · RAM " + (self.WorkingSet64 / 1024 / 1024) + " MB · private " + (self.PrivateMemorySize64 / 1024 / 1024) + " MB");
            }
            catch { }
            WriteLocked("INFO", "  TEMP = " + Path.GetTempPath() + " · trống " + FreeSpace(Path.GetTempPath()));
        }

        // ---------------- mô tả ----------------

        /// <summary>Kiểu, HRESULT, thông báo, chuỗi inner và stack — đủ để chẩn đoán từ xa.</summary>
        public static string Describe(Exception ex)
        {
            if (ex == null) return "";
            var text = new StringBuilder();
            int depth = 0;
            for (Exception current = ex; current != null && depth < 6; current = current.InnerException, depth++)
            {
                if (depth > 0) text.AppendLine("--- inner ---");
                text.Append(current.GetType().FullName)
                    .Append(" HRESULT=0x").Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture))
                    .Append(HResultName(current.HResult))
                    .Append(": ").AppendLine(current.Message);
                if (!string.IsNullOrEmpty(current.StackTrace)) text.AppendLine(current.StackTrace);
            }
            return text.ToString().TrimEnd();
        }

        private static string HResultName(int hr)
        {
            switch (unchecked((uint)hr))
            {
                case 0x80010105: return " (RPC_E_SERVERFAULT — SolidWorks ném lỗi trong lệnh COM)";
                case 0x8001010A: return " (RPC_E_SERVERCALL_RETRYLATER — SolidWorks bận)";
                case 0x80010001: return " (RPC_E_CALL_REJECTED — SolidWorks từ chối cuộc gọi)";
                case 0x800706BA: return " (RPC_S_SERVER_UNAVAILABLE — SolidWorks đã thoát/treo)";
                case 0x800706BE: return " (RPC_S_CALL_FAILED)";
                case 0x80004005: return " (E_FAIL)";
                case 0x80004002: return " (E_NOINTERFACE)";
                case 0x80070057: return " (E_INVALIDARG)";
                case 0x80004003: return " (E_POINTER)";
                case 0x80070005: return " (E_ACCESSDENIED)";
                case 0x80070020: return " (ERROR_SHARING_VIOLATION — file đang bị mở)";
                case 0x80131509: return " (InvalidOperation)";
                default: return "";
            }
        }

        /// <summary>Tồn tại? cỡ, giờ sửa, chỉ đọc — cho file nguồn và file ra.</summary>
        public static string DescribeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "(rỗng)";
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return path + " — KHÔNG TỒN TẠI";
                return path + " — " + info.Length.ToString("N0", CultureInfo.InvariantCulture) + " byte, sửa " +
                       info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                       ((info.Attributes & FileAttributes.ReadOnly) != 0 ? ", CHỈ ĐỌC" : "") +
                       ((info.Attributes & FileAttributes.Offline) != 0 || (((int)info.Attributes) & 0x400000) != 0
                           ? ", OneDrive chưa tải về (online-only)" : "");
            }
            catch (Exception ex)
            {
                return path + " — không đọc được: " + ex.Message;
            }
        }

        /// <summary>
        /// Thống kê một file DXF ASCII không cần netDxf: phiên bản, đơn vị, số đối tượng
        /// theo layer × loại trong ENTITIES. Đủ để thấy CUT/BEND/LASER_MARK có bao nhiêu nét
        /// ở DXF thô và DXF cuối mà không phải gửi file DXF.
        /// </summary>
        public static string DescribeDxf(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "(không có file DXF)";
            try
            {
                long size = new FileInfo(path).Length;
                var layers = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                string version = "?", units = "?";
                int blocks = 0, total = 0;
                using (var reader = new StreamReader(path, Encoding.Default, true))
                {
                    char[] head = new char[22];
                    int read = reader.Read(head, 0, head.Length);
                    if (read >= 18 && new string(head, 0, 18) == "AutoCAD Binary DXF")
                        return size.ToString("N0", CultureInfo.InvariantCulture) + " byte, DXF NHỊ PHÂN (không thống kê)";
                    reader.DiscardBufferedData();
                    reader.BaseStream.Position = 0;

                    string section = null, entity = null, layer = null, pendingHeader = null;
                    while (true)
                    {
                        string codeLine = reader.ReadLine();
                        if (codeLine == null) break;
                        string value = reader.ReadLine();
                        if (value == null) break;
                        if (!int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code)) continue;
                        value = value.Trim();

                        if (code == 0)
                        {
                            if (entity != null) { Count(layers, layer ?? "0", entity); total++; }
                            entity = null;
                            layer = null;
                            if (value == "SECTION") { section = null; continue; }
                            if (value == "ENDSEC") { section = null; continue; }
                            if (section == "ENTITIES" && value != "EOF") entity = value;
                            if (section == "BLOCKS" && value == "BLOCK") blocks++;
                            continue;
                        }
                        if (code == 2 && section == null) { section = value; continue; }
                        if (section == "HEADER")
                        {
                            if (code == 9) { pendingHeader = value; continue; }
                            if (pendingHeader == "$ACADVER" && code == 1) version = value;
                            if (pendingHeader == "$INSUNITS" && code == 70) units = value == "4" ? "mm" : value == "1" ? "inch" : value;
                            continue;
                        }
                        if (entity != null && code == 8) layer = value;
                    }
                }
                var text = new StringBuilder();
                text.Append(size.ToString("N0", CultureInfo.InvariantCulture)).Append(" byte, ").Append(version)
                    .Append(", $INSUNITS=").Append(units).Append(", ").Append(total).Append(" đối tượng, ")
                    .Append(blocks).Append(" block");
                foreach (var pair in layers)
                {
                    text.Append(" | ").Append(pair.Key).Append(": ")
                        .Append(string.Join(" ", pair.Value.Select(kind => kind.Key + "×" + kind.Value)));
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "không đọc được DXF: " + ex.Message;
            }
        }

        private static void Count(SortedDictionary<string, SortedDictionary<string, int>> layers, string layer, string kind)
        {
            if (!layers.TryGetValue(layer, out SortedDictionary<string, int> kinds))
                layers[layer] = kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            kinds.TryGetValue(kind, out int count);
            kinds[kind] = count + 1;
        }

        /// <summary>Tên các bit đang bật của một mã cờ (lỗi SaveAs/OpenDoc của SolidWorks).</summary>
        public static string DescribeFlags(Type flagsEnum, int value)
        {
            if (value == 0) return "0";
            var names = new List<string>();
            int rest = value;
            try
            {
                foreach (object item in Enum.GetValues(flagsEnum))
                {
                    int bit = Convert.ToInt32(item, CultureInfo.InvariantCulture);
                    if (bit != 0 && (value & bit) == bit)
                    {
                        names.Add(Enum.GetName(flagsEnum, item));
                        rest &= ~bit;
                    }
                }
            }
            catch { }
            if (rest != 0) names.Add("0x" + rest.ToString("X", CultureInfo.InvariantCulture));
            return value + " (" + string.Join("|", names) + ")";
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "(null)";
            if (value is string s) return s.Length == 0 ? "(rỗng)" : s;
            if (value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return value.ToString();
        }

        private static string Seconds(long milliseconds) =>
            (milliseconds / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + "s";

        private static string FreeSpace(string path)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(path));
                return (drive.AvailableFreeSpace / 1024 / 1024).ToString("N0", CultureInfo.InvariantCulture) + " MB";
            }
            catch { return "?"; }
        }

        private static string Sanitize(string value)
        {
            var text = new StringBuilder();
            foreach (char c in value) text.Append(char.IsLetterOrDigit(c) || c == '-' ? char.ToLowerInvariant(c) : '_');
            return text.ToString();
        }

        private static void Prune()
        {
            try
            {
                var files = new DirectoryInfo(Folder).GetFiles("dxf-*.log")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Skip(KeepSessionFiles)
                    .ToList();
                foreach (FileInfo file in files)
                {
                    try { file.Delete(); } catch { }
                }
            }
            catch { }
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class SessionScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                EndSession();
            }
        }

        private sealed class BlockScope : IDisposable
        {
            private readonly string _title;
            private readonly long _start;
            private bool _disposed;

            public BlockScope(string title, long start)
            {
                _title = title;
                _start = start;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                lock (Gate)
                {
                    if (_writer == null) return;
                    long now = _sessionClock.ElapsedMilliseconds;
                    WriteLocked("STEP", "▶ (hết khối)   (bước trước " + Seconds(now - _lastMark) + ")");
                    _indent = Math.Max(0, _indent - 1);
                    WriteLocked("INFO", "└─ " + _title + " · " + Seconds(now - _start));
                    _lastMark = now;
                }
            }
        }
    }
}
