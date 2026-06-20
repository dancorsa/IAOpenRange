// ORBTradeJournal.cs - Historial de trades para aprendizaje continuo (Capa IA #2)
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
    /// y analisis post-trade de la Capa 5.
    /// </summary>
    public class ORBTradeRecord
    {
        // --- Campos originales (columnas 0-15) ---
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

        // --- Campos extendidos (columnas 16-39) ---
        public double   EntryPrice           { get; set; }
        public double   ExitPrice            { get; set; }
        public int      Contracts            { get; set; }
        public double   PnlUsd              { get; set; }
        public double   PnlTicks            { get; set; }
        public double   StopTicks           { get; set; }
        public bool     Tp1Hit              { get; set; }
        public bool     Tp2Hit              { get; set; }
        public bool     Tp3Hit              { get; set; }
        public string   DayBias             { get; set; }
        public double   RsiM5AtEntry        { get; set; }
        public double   MacdHistAtEntry     { get; set; }
        public int      ClearanceTicks      { get; set; }
        public int      BarsSinceBreakout   { get; set; }
        public bool     WasReEntry          { get; set; }
        public double   AiRiskAdjustment    { get; set; }
        public string   AiReason            { get; set; }
        public bool     RiskGuardTriggered  { get; set; }
        public string   RiskGuardAction     { get; set; }
        public double   ConfCalibrationError{ get; set; }
        public string   PostTradeLesson     { get; set; }
        public int      ConsecLossesAtEntry { get; set; }
        public double   SessionMinConf      { get; set; }
        public string   PatternsFailing     { get; set; }
    }

    /// <summary>
    /// Resumen de performance para un periodo de tiempo.
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

        // Cabecera CSV (columnas 0-15 originales + 16-39 extendidas)
        private const string CSV_HEADER =
            "Date,Direction,AiConfidence,AiFakeoutProb,Result,RMultiple,ExitReason," +
            "DayOfWeek,GapDirection,VolumeRatio,OrbRangeTicks,PatternTag," +
            "MaxFavorableTicks,MaxAdverseTicks,DailyRegime,RegimeConviction," +
            "EntryPrice,ExitPrice,Contracts,PnlUsd,PnlTicks,StopTicks," +
            "Tp1Hit,Tp2Hit,Tp3Hit,DayBias,RsiM5,MacdHist,ClearanceTicks,BarsSinceBreakout," +
            "WasReEntry,AiRiskAdj,AiReason,RiskGuardTriggered,RiskGuardAction," +
            "ConfCalibError,PostTradeLesson,ConsecLossesAtEntry,SessionMinConf,PatternsFailing";

        #endregion

        #region Constructor

        /// <summary>
        /// Inicializa el journal para un instrumento especifico.
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

        #region Metodos p  blicos

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
                    _log("[Journal] No existe archivo previo. Se creara al primer trade.");
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
        /// Devuelve los   ltimos N trades para la Capa 2.
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
        /// Calcula metricas de performance de los   ltimos N dias.
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

                // Perdidas consecutivas desde el   ltimo trade hacia atras
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

        #region Metodos privados

        /// <summary>
        /// Persiste un   nico trade al archivo CSV (modo append).
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
                        // columnas 0-15
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
                        t.RegimeConviction.ToString("F4"),
                        // columnas 16-39
                        t.EntryPrice.ToString("F4"),
                        t.ExitPrice.ToString("F4"),
                        t.Contracts.ToString(),
                        t.PnlUsd.ToString("F2"),
                        t.PnlTicks.ToString("F2"),
                        t.StopTicks.ToString("F2"),
                        t.Tp1Hit ? "1" : "0",
                        t.Tp2Hit ? "1" : "0",
                        t.Tp3Hit ? "1" : "0",
                        EscapeCsv(t.DayBias ?? ""),
                        t.RsiM5AtEntry.ToString("F2"),
                        t.MacdHistAtEntry.ToString("F6"),
                        t.ClearanceTicks.ToString(),
                        t.BarsSinceBreakout.ToString(),
                        t.WasReEntry ? "1" : "0",
                        t.AiRiskAdjustment.ToString("F4"),
                        EscapeCsv(t.AiReason ?? ""),
                        t.RiskGuardTriggered ? "1" : "0",
                        EscapeCsv(t.RiskGuardAction ?? ""),
                        t.ConfCalibrationError.ToString("F4"),
                        EscapeCsv(t.PostTradeLesson ?? ""),
                        t.ConsecLossesAtEntry.ToString(),
                        t.SessionMinConf.ToString("F4"),
                        EscapeCsv(t.PatternsFailing ?? "")
                    ));
                }
            }
            catch (Exception ex)
            {
                _log($"[Journal] ERROR guardando trade en disco: {ex.Message}");
            }
        }

        /// <summary>Parsea una linea CSV al ORBTradeRecord correspondiente.</summary>
        private ORBTradeRecord ParseCsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                var cols = SplitCsvLine(line);
                if (cols.Length < 16) return null;

                var r = new ORBTradeRecord
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

                // columnas extendidas (archivos anteriores pueden no tenerlas)
                if (cols.Length > 16) r.EntryPrice           = ParseDouble(cols[16]);
                if (cols.Length > 17) r.ExitPrice            = ParseDouble(cols[17]);
                if (cols.Length > 18) r.Contracts            = (int)ParseDouble(cols[18]);
                if (cols.Length > 19) r.PnlUsd               = ParseDouble(cols[19]);
                if (cols.Length > 20) r.PnlTicks             = ParseDouble(cols[20]);
                if (cols.Length > 21) r.StopTicks            = ParseDouble(cols[21]);
                if (cols.Length > 22) r.Tp1Hit               = cols[22] == "1";
                if (cols.Length > 23) r.Tp2Hit               = cols[23] == "1";
                if (cols.Length > 24) r.Tp3Hit               = cols[24] == "1";
                if (cols.Length > 25) r.DayBias              = cols[25];
                if (cols.Length > 26) r.RsiM5AtEntry         = ParseDouble(cols[26]);
                if (cols.Length > 27) r.MacdHistAtEntry       = ParseDouble(cols[27]);
                if (cols.Length > 28) r.ClearanceTicks        = (int)ParseDouble(cols[28]);
                if (cols.Length > 29) r.BarsSinceBreakout     = (int)ParseDouble(cols[29]);
                if (cols.Length > 30) r.WasReEntry            = cols[30] == "1";
                if (cols.Length > 31) r.AiRiskAdjustment      = ParseDouble(cols[31]);
                if (cols.Length > 32) r.AiReason              = cols[32];
                if (cols.Length > 33) r.RiskGuardTriggered    = cols[33] == "1";
                if (cols.Length > 34) r.RiskGuardAction       = cols[34];
                if (cols.Length > 35) r.ConfCalibrationError  = ParseDouble(cols[35]);
                if (cols.Length > 36) r.PostTradeLesson       = cols[36];
                if (cols.Length > 37) r.ConsecLossesAtEntry   = (int)ParseDouble(cols[37]);
                if (cols.Length > 38) r.SessionMinConf        = ParseDouble(cols[38]);
                if (cols.Length > 39) r.PatternsFailing       = cols[39];

                return r;
            }
            catch
            {
                return null; // linea corrupta - ignorar
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

