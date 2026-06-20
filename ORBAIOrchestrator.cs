// ORBAIOrchestrator.cs â€" Orquestador de las 5 capas de IA del sistema ORB
// Parte del sistema IAOpenRange para NinjaTrader 8
// Proveedor configurable: Claude (Anthropic) u OpenAI

#region Usings
using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace System.Text.Json
{
    public enum JsonValueKind { Undefined, Object, Array, String, Number, True, False, Null }

    public sealed class JsonDocument : IDisposable
    {
        public JsonElement RootElement { get; private set; }
        private JsonDocument(object value) { RootElement = new JsonElement(value); }
        public static JsonDocument Parse(string json) { return new JsonDocument(new Parser(json).ParseValue()); }
        public void Dispose() { }
    }

    public struct JsonElement
    {
        private readonly object _value;
        internal JsonElement(object value) { _value = value; }
        public JsonValueKind ValueKind
        {
            get
            {
                if (_value == null) return JsonValueKind.Null;
                if (_value is Dictionary<string, object>) return JsonValueKind.Object;
                if (_value is List<object>) return JsonValueKind.Array;
                if (_value is string) return JsonValueKind.String;
                if (_value is bool) return (bool)_value ? JsonValueKind.True : JsonValueKind.False;
                if (_value is double || _value is float || _value is decimal || _value is int || _value is long) return JsonValueKind.Number;
                return JsonValueKind.Undefined;
            }
        }
        public bool TryGetProperty(string name, out JsonElement value)
        {
            var obj = _value as Dictionary<string, object>;
            object found;
            if (obj != null && obj.TryGetValue(name, out found)) { value = new JsonElement(found); return true; }
            value = new JsonElement(null); return false;
        }
        public JsonElement GetProperty(string name)
        {
            JsonElement value;
            if (TryGetProperty(name, out value)) return value;
            throw new KeyNotFoundException(name);
        }
        public IEnumerable<JsonElement> EnumerateArray()
        {
            var arr = _value as List<object>;
            if (arr == null) yield break;
            foreach (var item in arr) yield return new JsonElement(item);
        }
        public string GetString() { return _value == null ? null : Convert.ToString(_value, CultureInfo.InvariantCulture); }
        public double GetDouble() { return Convert.ToDouble(_value, CultureInfo.InvariantCulture); }
        public bool TryGetDouble(out double value)
        {
            try { value = GetDouble(); return true; }
            catch { value = 0; return false; }
        }
    }

    public static class JsonSerializer
    {
        public static string Serialize(object value) { return Write(value); }
        private static string Write(object value)
        {
            if (value == null) return "null";
            if (value is string || value is char) return Quote(Convert.ToString(value, CultureInfo.InvariantCulture));
            if (value is bool) return (bool)value ? "true" : "false";
            if (value is Enum) return Quote(value.ToString());
            if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is float || value is double || value is decimal)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            var dictionary = value as IDictionary;
            if (dictionary != null)
            {
                var parts = new List<string>();
                foreach (DictionaryEntry item in dictionary)
                    parts.Add(Quote(Convert.ToString(item.Key, CultureInfo.InvariantCulture)) + ":" + Write(item.Value));
                return "{" + string.Join(",", parts) + "}";
            }
            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                var parts = new List<string>();
                foreach (var item in enumerable) parts.Add(Write(item));
                return "[" + string.Join(",", parts) + "]";
            }
            var props = value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var fields = new List<string>();
            foreach (var prop in props)
                if (prop.GetIndexParameters().Length == 0)
                    fields.Add(Quote(prop.Name) + ":" + Write(prop.GetValue(value, null)));
            return "{" + string.Join(",", fields) + "}";
        }
        private static string Quote(string text)
        {
            if (text == null) return "null";
            return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }
    }

    internal sealed class Parser
    {
        private readonly string _json; private int _pos;
        public Parser(string json) { _json = json ?? ""; }
        public object ParseValue()
        {
            SkipWhite(); if (_pos >= _json.Length) return null;
            char c = _json[_pos];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return ParseString();
            if (c == 't') { _pos += 4; return true; }
            if (c == 'f') { _pos += 5; return false; }
            if (c == 'n') { _pos += 4; return null; }
            return ParseNumber();
        }
        private Dictionary<string, object> ParseObject()
        {
            var obj = new Dictionary<string, object>(); _pos++; SkipWhite();
            if (Peek('}')) { _pos++; return obj; }
            while (_pos < _json.Length)
            {
                string key = ParseString(); SkipWhite(); if (Peek(':')) _pos++;
                obj[key] = ParseValue(); SkipWhite();
                if (Peek('}')) { _pos++; break; }
                if (Peek(',')) _pos++;
            }
            return obj;
        }
        private List<object> ParseArray()
        {
            var arr = new List<object>(); _pos++; SkipWhite();
            if (Peek(']')) { _pos++; return arr; }
            while (_pos < _json.Length)
            {
                arr.Add(ParseValue()); SkipWhite();
                if (Peek(']')) { _pos++; break; }
                if (Peek(',')) _pos++;
            }
            return arr;
        }
        private string ParseString()
        {
            var sb = new StringBuilder(); if (Peek('"')) _pos++;
            while (_pos < _json.Length)
            {
                char c = _json[_pos++];
                if (c == '"') break;
                if (c == '\\' && _pos < _json.Length)
                {
                    char e = _json[_pos++];
                    if (e == 'n') sb.Append('\n'); else if (e == 'r') sb.Append('\r'); else if (e == 't') sb.Append('\t'); else if (e == 'b') sb.Append('\b'); else if (e == 'f') sb.Append('\f'); else if (e == 'u' && _pos + 4 <= _json.Length) { sb.Append((char)int.Parse(_json.Substring(_pos, 4), NumberStyles.HexNumber)); _pos += 4; } else sb.Append(e);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
        private double ParseNumber()
        {
            int start = _pos;
            while (_pos < _json.Length && "-+0123456789.eE".IndexOf(_json[_pos]) >= 0) _pos++;
            double value; return double.TryParse(_json.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }
        private void SkipWhite() { while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos])) _pos++; }
        private bool Peek(char c) { SkipWhite(); return _pos < _json.Length && _json[_pos] == c; }
    }
}
namespace NinjaTrader.NinjaScript.Strategies
{
    #region Enums y DTOs de respuesta

    public enum ORBAIProvider { Claude, OpenAI, Disabled }

    // --- Capa 1 ---
    public class RegimeAnalysis
    {
        public string Regime          { get; set; } = "uncertain";
        public double Conviction      { get; set; } = 0.5;
        public bool   FavorableForOrb { get; set; } = true;
        public double MaxRiskToday    { get; set; } = 1.0;
        public List<string> AvoidDirections { get; set; } = new List<string>();
        public string RegimeReason    { get; set; } = "";
        public bool   IsValid         { get; set; } = false;
    }

    // --- Capa 2 ---
    public class LearningAdjustment
    {
        public double AdjustedMinConfidence { get; set; } = 0.65;
        public List<string> PatternsWorking { get; set; } = new List<string>();
        public List<string> PatternsFailing { get; set; } = new List<string>();
        public string SessionGuidance       { get; set; } = "";
        public bool   IsValid               { get; set; } = false;
    }

    // --- Capa 3 ---
    public class EntrySignalValidation
    {
        public bool   Approve            { get; set; } = false;
        public double Confidence         { get; set; } = 0.0;
        public string Reason             { get; set; } = "";
        public double RiskAdjustment     { get; set; } = 1.0;
        public double FakeoutProbability { get; set; } = 1.0;
        public bool   IsValid            { get; set; } = false;
    }

    // --- Capa 4 ---
    public class RiskGuardAction
    {
        public string Action               { get; set; } = "hold";
        public string Urgency              { get; set; } = "low";
        public double? NewStopDistanceTicks{ get; set; } = null;
        public string Reasoning            { get; set; } = "";
        public bool   IsValid              { get; set; } = false;
    }

    // --- Capa 5 ---
    public class PostTradeAnalysis
    {
        public string PrimaryFailureReason       { get; set; } = null;
        public string PatternTag                 { get; set; } = "";
        public double ConfidenceCalibrationError { get; set; } = 0.0;
        public string Lesson                     { get; set; } = "";
        public bool   IsValid                    { get; set; } = false;
    }

    // --- Payloads de entrada ---
    public class DailyContextData
    {
        public string Date                { get; set; }
        public string Instrument          { get; set; }
        public double PrevDayRangeTicks   { get; set; }
        public double PrevDayVolumeVsAvg  { get; set; }
        public double GlobexRangeTicks    { get; set; }
        public double GapPct              { get; set; }
        public string DayOfWeek           { get; set; }
        public bool   FedEventToday       { get; set; }
        public bool   IsDayBeforeHoliday  { get; set; }
    }

    public class ORBSignalPayload
    {
        public string Instrument              { get; set; }
        public string Timestamp               { get; set; }
        public string SignalDirection         { get; set; }
        public double OrbHigh                 { get; set; }
        public double OrbLow                  { get; set; }
        public double OrbRangeTicks           { get; set; }
        public double OrbRangeVsAtrPct        { get; set; }
        public double BreakoutBarClose        { get; set; }
        public double BreakoutConfirmTicks    { get; set; }
        public int    BarsSinceBreakout       { get; set; }
        public double GapPct                  { get; set; }
        public string GapDirection            { get; set; }
        public double GlobexRangeTicks        { get; set; }
        public bool   BreakoutIsOutsideGlobex { get; set; }
        public double VolumeRatioOpen         { get; set; }
        public string DayBias                 { get; set; }
        public double RsiM5                   { get; set; }
        public double MacdHistM5              { get; set; }
        public double VwapCurrent             { get; set; }
        public string EntryVsVwap             { get; set; }
        public double ClearanceTicks          { get; set; }
        public double ProposedEntry           { get; set; }
        public double ProposedStop            { get; set; }
        public double ProposedTarget1         { get; set; }
        public double ProposedTarget2         { get; set; }
        public double ProposedTarget3         { get; set; }
        public double RiskRewardT1            { get; set; }
        public double RiskRewardT2            { get; set; }
        public string DailyRegime             { get; set; }
        public double RegimeConviction        { get; set; }
        public double SessionMinConfidence    { get; set; }
        public List<string> PatternsFailingToday { get; set; } = new List<string>();
    }

    public class RiskGuardPayload
    {
        public string Trigger                  { get; set; }
        public double TriggerMagnitude         { get; set; }
        public string OpenPositionDirection    { get; set; }
        public double OpenPositionPnlTicks     { get; set; }
        public int    OpenPositionBarsHeld     { get; set; }
        public double MarketMoveLastBarTicks   { get; set; }
        public double CurrentStopDistanceTicks { get; set; }
    }

    public class ClosedTradePayload
    {
        public ORBSignalPayload EntryConditions       { get; set; }
        public double           AiConfidenceAtEntry   { get; set; }
        public double           AiFakeoutProbability  { get; set; }
        public string           ActualResult          { get; set; }
        public double           ActualRMultiple       { get; set; }
        public string           ExitReason            { get; set; }
        public int              BarsHeld              { get; set; }
        public double           MaxFavorableExcursion { get; set; }
        public double           MaxAdverseExcursion   { get; set; }
    }

    #endregion

    /// <summary>
    /// Orquesta las 5 capas de IA del sistema ORB.
    /// HttpClient instanciado una sola vez. Todas las llamadas son async.
    /// Las capas 1, 2 y 5 son fire-and-forget; las capas 3 y 4 son bloqueantes.
    /// </summary>
    public class ORBAIOrchestrator : IDisposable
    {
        #region Campos privados

        private readonly HttpClient       _http;
        private readonly Action<string>   _log;
        private readonly ORBAIProvider       _provider;
        private readonly string           _apiKey;
        private readonly string           _modelOverride;
        private readonly TimeSpan         _timeout = TimeSpan.FromSeconds(8);

        // URLs de los proveedores
        private const string CLAUDE_URL = "https://api.anthropic.com/v1/messages";
        private const string OPENAI_URL = "https://api.openai.com/v1/chat/completions";

        // Modelos por defecto
        private const string CLAUDE_MODEL = "claude-sonnet-4-6";
        private const string OPENAI_MODEL = "gpt-4o";

        private bool _disposed = false;

        #endregion

        #region Constructor y Dispose

        /// <summary>
        /// Inicializa el orquestador de IA.
        /// </summary>
        /// <param name="provider">Proveedor de IA (Claude, OpenAI o Disabled).</param>
        /// <param name="apiKey">API Key del proveedor seleccionado.</param>
        /// <param name="log">Delegado para logging.</param>
        /// <param name="modelOverride">Modelo personalizado (vacÃ­o = usar default del proveedor).</param>
        public ORBAIOrchestrator(ORBAIProvider provider, string apiKey,
                                 Action<string> log, string modelOverride = "")
        {
            _provider      = provider;
            _apiKey        = apiKey;
            _log           = log ?? (_ => { });
            _modelOverride = modelOverride;

            _http = new HttpClient { Timeout = _timeout };

            if (_provider == ORBAIProvider.Claude)
            {
                _http.DefaultRequestHeaders.Add("x-api-key", _apiKey);
                _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
            else if (_provider == ORBAIProvider.OpenAI)
            {
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            }

            _log($"[AI] Orquestador inicializado â€" Proveedor:{_provider} Modelo:{GetModelName()}");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _http?.Dispose();
                _disposed = true;
            }
        }

        #endregion

        #region CAPA 1 â€" AnÃ¡lisis de rÃ©gimen diario

        /// <summary>
        /// Analiza el rÃ©gimen del dÃ­a (tendencia/rango/alta volatilidad) antes de operar.
        /// Llamar UNA VEZ en OnSessionStart().
        /// </summary>
        public async Task<RegimeAnalysis> AnalyzeDailyRegimeAsync(DailyContextData data)
        {
            var fallback = new RegimeAnalysis
            {
                Regime = "uncertain", Conviction = 0.5, FavorableForOrb = true,
                MaxRiskToday = 1.0, IsValid = false, RegimeReason = "API no disponible"
            };

            if (_provider == ORBAIProvider.Disabled) return fallback;

            string systemPrompt =
                "Eres un analista de rÃ©gimen de mercado para futuros del CME. " +
                "RecibirÃ¡s contexto pre-apertura y debes evaluar si las condiciones " +
                "favorecen una estrategia de breakout (ORB) o si el mercado probablemente " +
                "estarÃ¡ en rango sin convicciÃ³n direccional.\n\n" +
                "Responde ÃšNICAMENTE con JSON vÃ¡lido (sin texto adicional, sin markdown):\n" +
                "{\n" +
                "  \"regime\": \"trending | ranging | high_volatility | uncertain\",\n" +
                "  \"conviction\": 0.0,\n" +
                "  \"favorable_for_orb\": true,\n" +
                "  \"max_risk_today\": 1.0,\n" +
                "  \"avoid_directions\": [],\n" +
                "  \"regime_reason\": \"string max 150 chars\"\n" +
                "}\n\n" +
                "Favorece ORB cuando: gap significativo presente, Globex range normal, " +
                "dÃ­a no es viernes tarde ni vÃ­spera de festivo, sin Fed en las prÃ³ximas 2h. " +
                "No favorece ORB cuando: gap_pct cercano a 0 con Globex muy amplio, " +
                "mÃºltiples earnings, viernes despuÃ©s de mediodÃ­a.";

            string userMsg = JsonSerializer.Serialize(new
            {
                call_type             = "daily_regime_analysis",
                date                  = data.Date,
                instrument            = data.Instrument,
                prev_day_range_ticks  = data.PrevDayRangeTicks,
                prev_day_volume_vs_avg= data.PrevDayVolumeVsAvg,
                globex_range_ticks    = data.GlobexRangeTicks,
                gap_pct               = data.GapPct,
                day_of_week           = data.DayOfWeek,
                fed_event_today       = data.FedEventToday,
                is_day_before_holiday = data.IsDayBeforeHoliday
            });

            try
            {
                string rawJson = await CallAIAsync(systemPrompt, userMsg);
                if (string.IsNullOrEmpty(rawJson)) return fallback;

                var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                var result = new RegimeAnalysis
                {
                    IsValid       = true,
                    Regime        = GetString(root, "regime", "uncertain"),
                    Conviction    = GetDouble(root, "conviction", 0.5),
                    FavorableForOrb = GetBool(root, "favorable_for_orb", true),
                    MaxRiskToday  = GetDouble(root, "max_risk_today", 1.0),
                    RegimeReason  = GetString(root, "regime_reason", ""),
                    AvoidDirections = new List<string>()
                };

                if (root.TryGetProperty("avoid_directions", out var dirs))
                    foreach (var d in dirs.EnumerateArray())
                        result.AvoidDirections.Add(d.GetString() ?? "");

                _log($"[AI-Capa1] RÃ©gimen:{result.Regime} Conv:{result.Conviction:F2} " +
                     $"ORB:{result.FavorableForOrb} MaxRisk:{result.MaxRiskToday:F2}");

                return result;
            }
            catch (Exception ex)
            {
                _log($"[AI-Capa1] ERROR: {ex.Message}");
                return fallback;
            }
        }

        #endregion

        #region CAPA 2 â€" Aprendizaje continuo

        /// <summary>
        /// Analiza los Ãºltimos N trades para ajustar el umbral de confianza mÃ­nimo del dÃ­a.
        /// Llamar UNA VEZ en OnSessionStart(), en paralelo con la Capa 1.
        /// </summary>
        public async Task<LearningAdjustment> AnalyzeRecentPerformanceAsync(
            List<ORBTradeRecord> last20Trades, string instrument,
            ORBPerformanceSummary summary)
        {
            var fallback = new LearningAdjustment
            {
                AdjustedMinConfidence = 0.65,
                SessionGuidance = "Sin datos suficientes para aprendizaje.",
                IsValid = false
            };

            if (_provider == ORBAIProvider.Disabled || last20Trades == null || last20Trades.Count < 3)
                return fallback;

            string systemPrompt =
                "Eres un analista de performance para una estrategia de Opening Range Breakout. " +
                "RecibirÃ¡s los Ãºltimos 20 trades con sus condiciones y resultados. " +
                "Identifica patrones de Ã©xito y fracaso para ajustar el criterio de hoy.\n\n" +
                "Responde ÃšNICAMENTE con JSON vÃ¡lido:\n" +
                "{\n" +
                "  \"adjusted_min_confidence\": 0.65,\n" +
                "  \"patterns_working\": [\"string\"],\n" +
                "  \"patterns_failing\": [\"string\"],\n" +
                "  \"session_guidance\": \"string max 200 chars\"\n" +
                "}\n\n" +
                "Si win_rate_20 < 0.45 â†’ sube adjusted_min_confidence a 0.75-0.85. " +
                "Si win_rate_20 > 0.65 â†’ puede bajar a 0.55-0.60. " +
                "Identifica patrones de fracaso repetidos (gap opuesto, viernes, bajo volumen).";

            // Serializar los Ãºltimos 20 trades de forma compacta
            var tradesJson = new List<object>();
            foreach (var t in last20Trades)
            {
                tradesJson.Add(new
                {
                    date              = t.Date.ToString("yyyy-MM-dd"),
                    direction         = t.Direction,
                    ai_confidence     = t.AiConfidenceAtEntry,
                    ai_fakeout_prob   = t.AiFakeoutProb,
                    result            = t.Result,
                    r_multiple        = t.RMultiple,
                    exit_reason       = t.ExitReason,
                    day_of_week       = t.DayOfWeek,
                    gap_direction     = t.GapDirection,
                    volume_ratio      = t.VolumeRatio,
                    orb_range_ticks   = t.OrbRangeTicks,
                    pattern_tag       = t.PatternTag
                });
            }

            string userMsg = JsonSerializer.Serialize(new
            {
                call_type               = "session_learning",
                instrument              = instrument,
                last_20_trades          = tradesJson,
                win_rate_7d             = summary.WinRate7d,
                avg_r_multiple_7d       = summary.AvgRMultiple7d,
                current_consecutive_losses = summary.CurrentConsecutiveLoss
            });

            try
            {
                string rawJson = await CallAIAsync(systemPrompt, userMsg);
                if (string.IsNullOrEmpty(rawJson)) return fallback;

                var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                var result = new LearningAdjustment
                {
                    IsValid               = true,
                    AdjustedMinConfidence = GetDouble(root, "adjusted_min_confidence", 0.65),
                    SessionGuidance       = GetString(root, "session_guidance", ""),
                    PatternsWorking       = new List<string>(),
                    PatternsFailing       = new List<string>()
                };

                if (root.TryGetProperty("patterns_working", out var pw))
                    foreach (var p in pw.EnumerateArray())
                        result.PatternsWorking.Add(p.GetString() ?? "");

                if (root.TryGetProperty("patterns_failing", out var pf))
                    foreach (var p in pf.EnumerateArray())
                        result.PatternsFailing.Add(p.GetString() ?? "");

                _log($"[AI-Capa2] MinConf ajustado:{result.AdjustedMinConfidence:F2} " +
                     $"GuÃ­a:{result.SessionGuidance}");

                return result;
            }
            catch (Exception ex)
            {
                _log($"[AI-Capa2] ERROR: {ex.Message}");
                return fallback;
            }
        }

        #endregion

        #region CAPA 3 â€" ValidaciÃ³n de entrada (bloqueante)

        /// <summary>
        /// Valida si una seÃ±al de breakout es genuina o probable fakeout.
        /// Llamada BLOQUEANTE â€" condiciona la decisiÃ³n de entrada.
        /// </summary>
        public async Task<EntrySignalValidation> ValidateEntryAsync(ORBSignalPayload payload)
        {
            var fallback = new EntrySignalValidation
            {
                Approve = false, Confidence = 0.0,
                Reason = "API no disponible â€" entrada rechazada por seguridad.",
                RiskAdjustment = 1.0, FakeoutProbability = 1.0, IsValid = false
            };

            if (_provider == ORBAIProvider.Disabled)
                return new EntrySignalValidation { Approve = true, Confidence = 0.70,
                    RiskAdjustment = 1.0, FakeoutProbability = 0.30, IsValid = true,
                    Reason = "IA deshabilitada â€" aprobaciÃ³n automÃ¡tica." };

            string systemPrompt =
                "Eres un analista cuantitativo especializado en estrategias de Opening Range Breakout " +
                "(ORB) en futuros del CME. RecibirÃ¡s datos de una seÃ±al de breakout junto con el " +
                "rÃ©gimen del dÃ­a y patrones histÃ³ricos recientes. Determina si es breakout genuino.\n\n" +
                "Responde ÃšNICAMENTE con JSON vÃ¡lido:\n" +
                "{\n" +
                "  \"approve\": true,\n" +
                "  \"confidence\": 0.0,\n" +
                "  \"reason\": \"string max 120 chars\",\n" +
                "  \"risk_adjustment\": 1.0,\n" +
                "  \"fakeout_probability\": 0.0\n" +
                "}\n\n" +
                "APROBACIÃ"N: orb_range_vs_atr_pct 30%-120%, breakout_confirm_ticks >= 2, " +
                "volume_ratio >= 1.20, breakout_is_outside_globex = true, rsi_m5 alineado, " +
                "gap alineado o flat, clearance_ticks >= 8, risk_reward_t1 >= 1.5, " +
                "daily_regime = trending o favorable.\n" +
                "RECHAZO: bars_since_breakout > 3, orb_range_vs_atr_pct > 150%, " +
                "volume_ratio < 0.70, gap opuesto, clearance < 5, " +
                "patrÃ³n coincide con patterns_failing_today (-0.20 extra en confidence).\n" +
                "Usa session_min_confidence como umbral de referencia, no un valor fijo.";

            string userMsg = JsonSerializer.Serialize(new
            {
                call_type                   = "entry_validation",
                instrument                  = payload.Instrument,
                timestamp                   = payload.Timestamp,
                signal_direction            = payload.SignalDirection,
                orb_high                    = payload.OrbHigh,
                orb_low                     = payload.OrbLow,
                orb_range_ticks             = payload.OrbRangeTicks,
                orb_range_vs_atr_pct        = payload.OrbRangeVsAtrPct,
                breakout_bar_close          = payload.BreakoutBarClose,
                breakout_confirm_ticks      = payload.BreakoutConfirmTicks,
                bars_since_breakout         = payload.BarsSinceBreakout,
                gap_pct                     = payload.GapPct,
                gap_direction               = payload.GapDirection,
                globex_range_ticks          = payload.GlobexRangeTicks,
                breakout_is_outside_globex  = payload.BreakoutIsOutsideGlobex,
                volume_ratio_open           = payload.VolumeRatioOpen,
                day_bias                    = payload.DayBias,
                rsi_m5                      = payload.RsiM5,
                macd_hist_m5                = payload.MacdHistM5,
                vwap_current                = payload.VwapCurrent,
                entry_vs_vwap               = payload.EntryVsVwap,
                clearance_ticks             = payload.ClearanceTicks,
                proposed_entry              = payload.ProposedEntry,
                proposed_stop               = payload.ProposedStop,
                proposed_target_1           = payload.ProposedTarget1,
                proposed_target_2           = payload.ProposedTarget2,
                proposed_target_3           = payload.ProposedTarget3,
                risk_reward_t1              = payload.RiskRewardT1,
                risk_reward_t2              = payload.RiskRewardT2,
                daily_regime                = payload.DailyRegime,
                regime_conviction           = payload.RegimeConviction,
                session_min_confidence      = payload.SessionMinConfidence,
                patterns_failing_today      = payload.PatternsFailingToday
            });

            try
            {
                string rawJson = await CallAIAsync(systemPrompt, userMsg);
                if (string.IsNullOrEmpty(rawJson)) return fallback;

                var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                var result = new EntrySignalValidation
                {
                    IsValid            = true,
                    Approve            = GetBool(root, "approve", false),
                    Confidence         = GetDouble(root, "confidence", 0.0),
                    Reason             = GetString(root, "reason", ""),
                    RiskAdjustment     = GetDouble(root, "risk_adjustment", 1.0),
                    FakeoutProbability = GetDouble(root, "fakeout_probability", 1.0)
                };

                _log($"[AI-Capa3] Aprobado:{result.Approve} Conf:{result.Confidence:F2} " +
                     $"Fakeout:{result.FakeoutProbability:F2} RazÃ³n:{result.Reason}");

                return result;
            }
            catch (Exception ex)
            {
                _log($"[AI-Capa3] ERROR: {ex.Message}");
                return fallback;
            }
        }

        #endregion

        #region CAPA 4 â€" Guardia de riesgo sistÃ©mico (bloqueante)

        /// <summary>
        /// EvalÃºa si una condiciÃ³n de mercado anÃ³mala requiere ajuste de stop o cierre.
        /// Llamada BLOQUEANTE cuando hay posiciÃ³n abierta y se detecta anomalÃ­a.
        /// </summary>
        public async Task<RiskGuardAction> CheckSystemicRiskAsync(RiskGuardPayload payload)
        {
            var fallback = new RiskGuardAction
            {
                Action = "hold", Urgency = "low",
                Reasoning = "API no disponible â€" mantener posiciÃ³n.", IsValid = false
            };

            if (_provider == ORBAIProvider.Disabled) return fallback;

            string systemPrompt =
                "Eres un sistema de guardia de riesgo para una posiciÃ³n abierta en futuros. " +
                "Se detectÃ³ una condiciÃ³n de mercado anÃ³mala. EvalÃºa si la posiciÃ³n debe " +
                "mantenerse, ajustarse o cerrarse inmediatamente.\n\n" +
                "Responde ÃšNICAMENTE con JSON vÃ¡lido:\n" +
                "{\n" +
                "  \"action\": \"hold | tighten_stop | close_immediately\",\n" +
                "  \"urgency\": \"low | medium | high | critical\",\n" +
                "  \"new_stop_distance_ticks\": null,\n" +
                "  \"reasoning\": \"string max 100 chars\"\n" +
                "}\n\n" +
                "close_immediately: trigger_magnitude > 3.0 Y posiciÃ³n va contra el movimiento. " +
                "tighten_stop: movimiento a favor pero con riesgo de reversiÃ³n violenta. " +
                "hold: movimiento anÃ³malo confirma la posiciÃ³n con bajo riesgo.";

            string userMsg = JsonSerializer.Serialize(new
            {
                call_type                    = "systemic_risk_check",
                trigger                      = payload.Trigger,
                trigger_magnitude            = payload.TriggerMagnitude,
                open_position_direction      = payload.OpenPositionDirection,
                open_position_pnl_ticks      = payload.OpenPositionPnlTicks,
                open_position_bars_held      = payload.OpenPositionBarsHeld,
                market_move_last_bar_ticks   = payload.MarketMoveLastBarTicks,
                current_stop_distance_ticks  = payload.CurrentStopDistanceTicks
            });

            try
            {
                string rawJson = await CallAIAsync(systemPrompt, userMsg);
                if (string.IsNullOrEmpty(rawJson)) return fallback;

                var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                var result = new RiskGuardAction
                {
                    IsValid   = true,
                    Action    = GetString(root, "action", "hold"),
                    Urgency   = GetString(root, "urgency", "low"),
                    Reasoning = GetString(root, "reasoning", "")
                };

                if (root.TryGetProperty("new_stop_distance_ticks", out var stopProp)
                    && stopProp.ValueKind != JsonValueKind.Null)
                    result.NewStopDistanceTicks = stopProp.GetDouble();

                _log($"[AI-Capa4] AcciÃ³n:{result.Action} Urgencia:{result.Urgency} " +
                     $"NuevoStop:{result.NewStopDistanceTicks} RazÃ³n:{result.Reasoning}");

                return result;
            }
            catch (Exception ex)
            {
                _log($"[AI-Capa4] ERROR: {ex.Message}");
                return fallback;
            }
        }

        #endregion

        #region CAPA 5 â€" AnÃ¡lisis post-trade (asÃ­ncrono, fire-and-forget)

        /// <summary>
        /// Analiza un trade cerrado para extraer lecciones y alimentar la Capa 2.
        /// Llamada ASÃNCRONA NO BLOQUEANTE â€" corre en background.
        /// </summary>
        public async Task<PostTradeAnalysis> AnalyzeClosedTradeAsync(ClosedTradePayload payload)
        {
            var fallback = new PostTradeAnalysis
            {
                PatternTag = "unknown", Lesson = "Sin anÃ¡lisis disponible.", IsValid = false
            };

            if (_provider == ORBAIProvider.Disabled) return fallback;

            string systemPrompt =
                "Eres un analista post-trade para una estrategia de Opening Range Breakout. " +
                "RecibirÃ¡s los detalles completos de un trade cerrado, incluyendo la predicciÃ³n " +
                "de la IA al entrar y lo que realmente ocurriÃ³. Analiza la causa raÃ­z.\n\n" +
                "Responde ÃšNICAMENTE con JSON vÃ¡lido:\n" +
                "{\n" +
                "  \"primary_failure_reason\": null,\n" +
                "  \"pattern_tag\": \"fakeout_on_gap_against | low_volume_entry | news_spike | " +
                "clean_breakout | momentum_exhaustion | range_day | time_stop\",\n" +
                "  \"confidence_calibration_error\": 0.0,\n" +
                "  \"lesson\": \"string max 150 chars\"\n" +
                "}\n\n" +
                "confidence_calibration_error: si confidence fue 0.85 y el trade perdiÃ³ = 0.7-0.8. " +
                "Si confidence fue 0.55 y perdiÃ³ = 0.1-0.2. Si ganÃ³ = 0.0-0.1.";

            var entrySnap = payload.EntryConditions;
            string userMsg = JsonSerializer.Serialize(new
            {
                call_type = "post_trade_analysis",
                entry_conditions = entrySnap == null ? null : new
                {
                    signal_direction           = entrySnap.SignalDirection,
                    orb_range_ticks            = entrySnap.OrbRangeTicks,
                    orb_range_vs_atr_pct       = entrySnap.OrbRangeVsAtrPct,
                    breakout_confirm_ticks     = entrySnap.BreakoutConfirmTicks,
                    bars_since_breakout        = entrySnap.BarsSinceBreakout,
                    gap_direction              = entrySnap.GapDirection,
                    volume_ratio_open          = entrySnap.VolumeRatioOpen,
                    day_bias                   = entrySnap.DayBias,
                    rsi_m5                     = entrySnap.RsiM5,
                    daily_regime               = entrySnap.DailyRegime
                },
                ai_prediction_at_entry = new
                {
                    confidence         = payload.AiConfidenceAtEntry,
                    fakeout_probability= payload.AiFakeoutProbability
                },
                actual_result                   = payload.ActualResult,
                actual_r_multiple               = payload.ActualRMultiple,
                exit_reason                     = payload.ExitReason,
                bars_held                       = payload.BarsHeld,
                max_favorable_excursion_ticks   = payload.MaxFavorableExcursion,
                max_adverse_excursion_ticks     = payload.MaxAdverseExcursion
            });

            try
            {
                string rawJson = await CallAIAsync(systemPrompt, userMsg);
                if (string.IsNullOrEmpty(rawJson)) return fallback;

                var doc = JsonDocument.Parse(rawJson);
                var root = doc.RootElement;

                var result = new PostTradeAnalysis
                {
                    IsValid = true,
                    PrimaryFailureReason       = root.TryGetProperty("primary_failure_reason", out var pfr)
                                                 && pfr.ValueKind != JsonValueKind.Null
                                                 ? pfr.GetString() : null,
                    PatternTag                 = GetString(root, "pattern_tag", "unknown"),
                    ConfidenceCalibrationError = GetDouble(root, "confidence_calibration_error", 0.0),
                    Lesson                     = GetString(root, "lesson", "")
                };

                _log($"[AI-Capa5] PatternTag:{result.PatternTag} " +
                     $"CalibErr:{result.ConfidenceCalibrationError:F2} LecciÃ³n:{result.Lesson}");

                return result;
            }
            catch (Exception ex)
            {
                _log($"[AI-Capa5] ERROR: {ex.Message}");
                return fallback;
            }
        }

        #endregion

        #region Motor HTTP comÃºn

        /// <summary>
        /// Realiza la llamada HTTP al proveedor de IA configurado.
        /// Maneja el formato de request/response para Claude y OpenAI.
        /// </summary>
        private async Task<string> CallAIAsync(string systemPrompt, string userMessage)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _log("[AI] API Key no configurada.");
                return null;
            }

            string requestBody;
            string model = GetModelName();

            if (_provider == ORBAIProvider.Claude)
            {
                requestBody = JsonSerializer.Serialize(new
                {
                    model      = model,
                    max_tokens = 512,
                    system     = systemPrompt,
                    messages   = new[] { new { role = "user", content = userMessage } }
                });
            }
            else // OpenAI
            {
                requestBody = JsonSerializer.Serialize(new
                {
                    model       = model,
                    max_tokens  = 512,
                    temperature = 0.1,
                    messages    = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userMessage  }
                    }
                });
            }

            string url = _provider == ORBAIProvider.Claude ? CLAUDE_URL : OPENAI_URL;

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var cts     = new CancellationTokenSource(_timeout);

            HttpResponseMessage response;
            try
            {
                response = await _http.PostAsync(url, content, cts.Token);
            }
            catch (TaskCanceledException)
            {
                _log("[AI] Timeout en llamada a API.");
                return null;
            }
            catch (Exception ex)
            {
                _log($"[AI] Error HTTP: {ex.Message}");
                return null;
            }

            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _log($"[AI] HTTP {(int)response.StatusCode}: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}");
                return null;
            }

            return ExtractTextFromResponse(responseBody);
        }

        /// <summary>
        /// Extrae el texto de la respuesta JSON del proveedor.
        /// Normaliza el formato entre Claude y OpenAI.
        /// </summary>
        private string ExtractTextFromResponse(string responseBody)
        {
            try
            {
                var doc  = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (_provider == ORBAIProvider.Claude)
                {
                    // Formato Claude: { "content": [{ "type": "text", "text": "..." }] }
                    if (root.TryGetProperty("content", out var contentArr))
                        foreach (var item in contentArr.EnumerateArray())
                            if (item.TryGetProperty("text", out var textProp))
                                return textProp.GetString()?.Trim();
                }
                else
                {
                    // Formato OpenAI: { "choices": [{ "message": { "content": "..." } }] }
                    if (root.TryGetProperty("choices", out var choices))
                        foreach (var choice in choices.EnumerateArray())
                            if (choice.TryGetProperty("message", out var msg))
                                if (msg.TryGetProperty("content", out var c))
                                    return c.GetString()?.Trim();
                }
            }
            catch (Exception ex)
            {
                _log($"[AI] Error parseando respuesta: {ex.Message}");
            }
            return null;
        }

        private string GetModelName()
        {
            if (!string.IsNullOrEmpty(_modelOverride)) return _modelOverride;
            return _provider == ORBAIProvider.Claude ? CLAUDE_MODEL : OPENAI_MODEL;
        }

        #endregion

        #region Helpers JSON

        private static string GetString(JsonElement el, string prop, string def)
        {
            return el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? def : def;
        }

        private static double GetDouble(JsonElement el, string prop, double def)
        {
            if (!el.TryGetProperty(prop, out var v)) return def;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)) return d;
            return def;
        }

        private static bool GetBool(JsonElement el, string prop, bool def)
        {
            if (!el.TryGetProperty(prop, out var v)) return def;
            if (v.ValueKind == JsonValueKind.True)  return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            return def;
        }

        #endregion
    }
}




