// ORBTradeJournal.cs â€” Historial de trades para aprendizaje continuo (Capa IA #2)
// Parte del sistema IAOpenRange para NinjaTrader 8

#region Usings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Registro estructurado de un trade cerrado, incluyendo condiciones de entrada
    /// y anÃ¡lisis post-trade de la Capa 5.
    /// </summary>
    public class ORBTradeRecord
    {
        public DateTime Date               { get; set; }
        public string   Direction          { get; set; }  // "LONG" o "SHORT"
        public double   AiConfidenceAtEntry{ get; set; }
        public double   AiFakeoutProb      { get; set; }
        public string   Result             { get; set; }  // "win" o "loss"
        public double   RMultiple          { get; set; }
        public string   ExitReason         { get; set; }
        public string   DayOfWeek          { get; set; }
        public string   GapDirection       { get; set; }
        public double   VolumeRatio        { get; set; }
        public double   OrbRangeTicks      { get; set; }
        public string   PatternTag         { get; set; }  // de Capa 5
        public double   MaxFavorableTicks  { get; set; }
        public double   MaxAdverseTicks    { get; set; }
        public string   DailyRegime        { get; set; }
        public double   RegimeConviction   { get; set; }
    }

    /// <summary>
    /// Resumen de performance para un perÃ­odo de tiempo.
    /// Devuelto por GetSummary() y enviado a la Capa 2.
    /// </summary>
    public class ORBPerformanceSummary
    {
        public double WinRate7d              { get; set; }
        public double AvgRMultiple7d         { get; set; }
        public int    CurrentConsecutiveLoss { get; set; }
        public int    TotalTrades            { get; set; }
    }

    /// <summary>
    /// Mantiene el historial de trades en memoria y en disco.
    /// Fuente de datos para la Capa IA #2 (aprendizaje continuo).
    /// </summary>
    public class ORBTradeJournal
    {
        #region Campos privados

        private readonly List<ORBTradeRecord> _trades;
        private readonly string            _filePath;
        private readonly Action<string>    _log;
        private readonly object            _lock = new object();

        // Cabecera CSV
        private const string CSV_HEADER =
            "Date,Direction,AiConfidence,AiFakeoutProb,Result,RMultiple,ExitReason," +
            "DayOfWeek,GapDirection,VolumeRatio,OrbRangeTicks,PatternTag," +
            "MaxFavorableTicks,MaxAdverseTicks,DailyRegime,RegimeConviction";

        #endregion

        #region Constructor

        /// <summary>
        /// Inicializa el journal para un instrumento especÃ­fico.
        /// </summary>
        /// <param name="instrument">Nombre del instrumento (ej. "ES 03-26").</param>
        /// <param name="log">Delegado para logging.</param>
        public ORBTradeJournal(string instrument, Action<string> log)
        {
            _log    = log ?? (_ => { });
            _trades = new List<ORBTradeRecord>();

            // Sanitizar nombre de instrumento para nombre de archivo
            var safeName = SanitizeFileName(instrument);
            var folder   = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "journal");

            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                _log($"[Journal] ERROR creando directorio journal: {ex.Message}");
            }

            _filePath = Path.Combine(folder, $"ORB_TradeJournal_{safeName}.csv");
            _log($"[Journal] Archivo de journal: {_filePath}");
        }

        #endregion

        #region MÃ©todos pÃºblicos

        /// <summary>
        /// Carga el historial desde el CSV al iniciar la estrategia.
        /// Llamar en OnStateChange(State.Configure).
        /// </summary>
        public void LoadFromDisk()
        {
            lock (_lock)
            {
                _trades.Clear();
                if (!File.Exists(_filePath))
                {
                    _log("[Journal] No existe archivo previo. Se crearÃ¡ al primer trade.");
                    return;
                }

                try
                {
                    var lines = File.ReadAllLines(_filePath, Encoding.UTF8);
                    int loaded = 0;

                    foreach (var line in lines.Skip(1)) // saltar cabecera
                    {
                        var record = ParseCsvLine(line);
                        if (record != null)
                        {
                            _trades.Add(record);
                            loaded++;
                        }
                    }

                    _log($"[Journal] Cargados {loaded} trades del historial.");
                }
                catch (Exception ex)
                {
                    _log($"[Journal] ERROR cargando historial: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Agrega un trade al historial en memoria y lo persiste en disco inmediatamente.
        /// </summary>
        public void AddTrade(ORBTradeRecord trade)
        {
            if (trade == null) return;

            lock (_lock)
            {
                _trades.Add(trade);
                SaveToDisk(trade);
            }

            _log($"[Journal] Trade registrado: {trade.Direction} {trade.Result} " +
                 $"R:{trade.RMultiple:F2} PatternTag:{trade.PatternTag}");
        }

        /// <summary>
        /// Devuelve los Ãºltimos N trades para la Capa 2.
        /// </summary>
        public List<ORBTradeRecord> GetLast(int n)
        {
            lock (_lock)
            {
                return _trades
                    .OrderByDescending(t => t.Date)
                    .Take(n)
                    .OrderBy(t => t.Date)
                    .ToList();
            }
        }

        /// <summary>
        /// Calcula mÃ©tricas de performance de los Ãºltimos N dÃ­as.
        /// </summary>
        public ORBPerformanceSummary GetSummary(int days)
        {
            lock (_lock)
            {
                var cutoff = DateTime.Now.Date.AddDays(-days);
                var recent = _trades.Where(t => t.Date.Date >= cutoff).ToList();

                var summary = new ORBPerformanceSummary { TotalTrades = recent.Count };

                if (recent.Count == 0)
                    return summary;

                var last7 = _trades.Where(t => t.Date.Date >= DateTime.Now.Date.AddDays(-7)).ToList();
                if (last7.Count > 0)
                {
                    summary.WinRate7d    = last7.Count(t => t.Result == "win") / (double)last7.Count;
                    summary.AvgRMultiple7d = last7.Average(t => t.RMultiple);
                }

                // PÃ©rdidas consecutivas desde el Ãºltimo trade hacia atrÃ¡s
                int consec = 0;
                foreach (var t in _trades.OrderByDescending(t => t.Date))
                {
                    if (t.Result == "loss") consec++;
                    else break;
                }
                summary.CurrentConsecutiveLoss = consec;

                return summary;
            }
        }

        /// <summary>
        /// Devuelve el conteo total de trades registrados.
        /// </summary>
        public int Count()
        {
            lock (_lock) { return _trades.Count; }
        }

        #endregion

        #region MÃ©todos privados

        /// <summary>
        /// Persiste un Ãºnico trade al archivo CSV (modo append).
        /// Si el archivo no existe, escribe la cabecera primero.
        /// </summary>
        private void SaveToDisk(ORBTradeRecord t)
        {
            try
            {
                bool writeHeader = !File.Exists(_filePath);
                using (var sw = new StreamWriter(_filePath, append: true, encoding: Encoding.UTF8))
                {
                    if (writeHeader)
                        sw.WriteLine(CSV_HEADER);

                    sw.WriteLine(string.Join(",",
                        EscapeCsv(t.Date.ToString("yyyy-MM-dd HH:mm:ss")),
                        EscapeCsv(t.Direction),
                        t.AiConfidenceAtEntry.ToString("F4"),
                        t.AiFakeoutProb.ToString("F4"),
                        EscapeCsv(t.Result),
                        t.RMultiple.ToString("F4"),
                        EscapeCsv(t.ExitReason),
                        EscapeCsv(t.DayOfWeek),
                        EscapeCsv(t.GapDirection),
                        t.VolumeRatio.ToString("F4"),
                        t.OrbRangeTicks.ToString("F1"),
                        EscapeCsv(t.PatternTag ?? ""),
                        t.MaxFavorableTicks.ToString("F1"),
                        t.MaxAdverseTicks.ToString("F1"),
                        EscapeCsv(t.DailyRegime ?? ""),
                        t.RegimeConviction.ToString("F4")
                    ));
                }
            }
            catch (Exception ex)
            {
                _log($"[Journal] ERROR guardando trade en disco: {ex.Message}");
            }
        }

        /// <summary>Parsea una lÃ­nea CSV al ORBTradeRecord correspondiente.</summary>
        private ORBTradeRecord ParseCsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                var cols = SplitCsvLine(line);
                if (cols.Length < 16) return null;

                return new ORBTradeRecord
                {
                    Date                = DateTime.Parse(cols[0]),
                    Direction           = cols[1],
                    AiConfidenceAtEntry = ParseDouble(cols[2]),
                    AiFakeoutProb       = ParseDouble(cols[3]),
                    Result              = cols[4],
                    RMultiple           = ParseDouble(cols[5]),
                    ExitReason          = cols[6],
                    DayOfWeek           = cols[7],
                    GapDirection        = cols[8],
                    VolumeRatio         = ParseDouble(cols[9]),
                    OrbRangeTicks       = ParseDouble(cols[10]),
                    PatternTag          = cols[11],
                    MaxFavorableTicks   = ParseDouble(cols[12]),
                    MaxAdverseTicks     = ParseDouble(cols[13]),
                    DailyRegime         = cols[14],
                    RegimeConviction    = ParseDouble(cols[15])
                };
            }
            catch
            {
                return null; // lÃ­nea corrupta â€” ignorar
            }
        }

        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuote = false;
            var current  = new StringBuilder();

            foreach (char c in line)
            {
                if (c == '"') { inQuote = !inQuote; continue; }
                if (c == ',' && !inQuote) { result.Add(current.ToString()); current.Clear(); continue; }
                current.Append(c);
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        private static string EscapeCsv(string val)
        {
            if (val == null) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n"))
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            return val;
        }

        private static double ParseDouble(string s)
        {
            return double.TryParse(s, System.Globalization.NumberStyles.Any,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out double v) ? v : 0;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        #endregion
    }
}

