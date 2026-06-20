// ORBContextFilter.cs - Filtros de contexto: gap, Globex, volumen, noticias, sesi n
// Parte del sistema IAOpenRange para NinjaTrader 8

#region Usings
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Enum para el sesgo direccional del d a basado en contexto macro (M15 vs VWAP previo).
    /// </summary>
    public enum DayBias { Bullish, Bearish, Neutral }

    /// <summary>
    /// Analiza y expone los filtros de contexto del d a:
    /// gap de apertura, rango Globex, ratio de volumen, tendencia del d a y noticias.
    /// </summary>
    public class ORBContextFilter
    {
        #region Propiedades p blicas

        // --- Gap ---
        /// <summary>Gap de apertura en porcentaje respecto al cierre anterior.</summary>
        public double GapPct { get; private set; }

        /// <summary>Direcci n del gap: "UP", "DOWN" o "FLAT".</summary>
        public string GapDirection { get; private set; }

        // --- Globex ---
        /// <summary>M ximo del rango Globex overnight.</summary>
        public double GlobexHigh { get; private set; }

        /// <summary>M nimo del rango Globex overnight.</summary>
        public double GlobexLow { get; private set; }

        /// <summary>Amplitud del rango Globex en ticks.</summary>
        public double GlobexRangeTicks { get; private set; }

        // --- Volumen ---
        /// <summary>Ratio de volumen apertura vs promedio 30 d as.</summary>
        public double VolumeRatio { get; private set; }

        /// <summary>Factor de tama o aplicable por volumen (0.60 - 1.0).</summary>
        public double VolumeSizeFactor { get; private set; }

        // --- Sesgo del d a ---
        /// <summary>Sesgo direccional del d a calculado con M15 vs VWAP del d a previo.</summary>
        public DayBias DayBias { get; private set; }

        // --- Estado general ---
        /// <summary>
        /// True si el contexto es globalmente favorable para operar:
        /// rango v lido, volumen m nimo, sin noticia inmediata, no es viernes tarde.
        /// </summary>
        public bool IsFavorableContext { get; private set; }

        /// <summary>True si actualmente hay bloqueo por noticia de alto impacto pr xima.</summary>
        public bool IsNewsBlocked { get; private set; }

        /// <summary>Minutos hasta la pr xima noticia de alto impacto (999 si no hay).</summary>
        public int MinutesToNextNews { get; private set; }

        #endregion

        #region Campos privados

        private readonly double _gapThresholdPct;
        private readonly int    _blockMinutesBeforeNews;
        private readonly double _tickSize;

        // Lista de horarios de noticias de alto impacto (en horas ET, ej: 8.5 = 08:30)
        private List<double> _newsTimesET;

        // VWAP del d a previo (referencia para DayBias)
        private double _prevDayVwap;

        // Cierre del d a previo (para calcular gap)
        private double _prevDayClose;

        private readonly Action<string> _log;

        #endregion

        #region Constructor

        /// <summary>
        /// Inicializa el filtro de contexto con los par metros configurados por el usuario.
        /// </summary>
        public ORBContextFilter(double gapThresholdPct, int blockMinutesBeforeNews,
                                double tickSize, Action<string> log)
        {
            _gapThresholdPct       = gapThresholdPct;
            _blockMinutesBeforeNews= blockMinutesBeforeNews;
            _tickSize              = tickSize;
            _log                   = log ?? (_ => { });
            _newsTimesET           = new List<double>();
            GapDirection           = "FLAT";
            DayBias                = DayBias.Neutral;
            VolumeRatio            = 1.0;
            VolumeSizeFactor       = 1.0;
            MinutesToNextNews      = 999;
        }

        #endregion

        #region M todos p blicos de configuraci n

        /// <summary>
        /// Establece los horarios de noticias desde el CSV de par metros del usuario.
        /// Formato esperado: "08:30,10:00,14:00"
        /// </summary>
        public void SetNewsTimes(string csvTimes)
        {
            _newsTimesET.Clear();
            if (string.IsNullOrWhiteSpace(csvTimes)) return;

            foreach (var part in csvTimes.Split(','))
            {
                var trimmed = part.Trim();
                if (DateTime.TryParseExact(trimmed, "HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                {
                    _newsTimesET.Add(dt.Hour + dt.Minute / 60.0);
                }
            }
            _log($"[ContextFilter] Noticias configuradas: {_newsTimesET.Count} horarios.");
        }

        /// <summary>
        /// Establece el cierre del d a previo para calcular el gap de apertura.
        /// Llamar en OnSessionStart() cuando se conoce el close previo.
        /// </summary>
        public void SetPrevDayClose(double prevClose)
        {
            _prevDayClose = prevClose;
        }

        /// <summary>
        /// Establece el VWAP del d a previo para calcular el sesgo del d a.
        /// </summary>
        public void SetPrevDayVwap(double prevVwap)
        {
            _prevDayVwap = prevVwap;
        }

        /// <summary>
        /// Calcula el gap de apertura en cuanto se conoce el primer precio del d a.
        /// </summary>
        public void CalculateGap(double openPrice)
        {
            if (_prevDayClose <= 0)
            {
                GapPct       = 0;
                GapDirection = "FLAT";
                return;
            }

            GapPct = ((openPrice - _prevDayClose) / _prevDayClose) * 100.0;

            if      (GapPct >  _gapThresholdPct) GapDirection = "UP";
            else if (GapPct < -_gapThresholdPct) GapDirection = "DOWN";
            else                                  GapDirection = "FLAT";

            _log($"[ContextFilter] Gap calculado: {GapPct:F2}%   {GapDirection}");
        }

        /// <summary>
        /// Actualiza el rango Globex con los datos overnight.
        /// Llamar durante la fase pre-market con High/Low de la sesi n Globex.
        /// </summary>
        public void UpdateGlobex(double globexHigh, double globexLow)
        {
            GlobexHigh       = globexHigh;
            GlobexLow        = globexLow;
            GlobexRangeTicks = Math.Round((globexHigh - globexLow) / _tickSize, 1);
            _log($"[ContextFilter] Globex - High:{globexHigh:F2} Low:{globexLow:F2} Range:{GlobexRangeTicks}t");
        }

        /// <summary>
        /// Actualiza el ratio de volumen (volumen apertura vs promedio 30 d as).
        /// Actualizar peri dicamente durante la primera hora.
        /// </summary>
        public void UpdateVolumeRatio(double currentVolume, double avgVolume30d)
        {
            if (avgVolume30d <= 0) { VolumeRatio = 1.0; VolumeSizeFactor = 1.0; return; }

            VolumeRatio = currentVolume / avgVolume30d;

            // Factor de tama o basado en volumen (secci n 4.3)
            if      (VolumeRatio >= 1.20) VolumeSizeFactor = 1.0;
            else if (VolumeRatio >= 0.80) VolumeSizeFactor = 1.0;
            else                          VolumeSizeFactor = 0.60;
        }

        /// <summary>
        /// Calcula el sesgo del d a comparando el precio M15 actual con el VWAP del d a previo.
        /// Llamar una vez por barra M15 entre 09:30 y 10:00 ET.
        /// </summary>
        public void UpdateDayBias(double currentPriceM15)
        {
            if (_prevDayVwap <= 0) { DayBias = DayBias.Neutral; return; }

            double diffTicks = (currentPriceM15 - _prevDayVwap) / _tickSize;

            if      (diffTicks >  5) DayBias = DayBias.Bullish;
            else if (diffTicks < -5) DayBias = DayBias.Bearish;
            else                     DayBias = DayBias.Neutral;
        }

        /// <summary>
        /// Eval a si el breakout est  fuera del rango Globex (se al de expansi n real).
        /// </summary>
        /// <param name="breakoutPrice">Precio del breakout (ORB_High para LONG, ORB_Low para SHORT).</param>
        /// <param name="isLong">True si es un breakout LONG.</param>
        public bool IsBreakoutOutsideGlobex(double breakoutPrice, bool isLong)
        {
            if (GlobexHigh <= 0 || GlobexLow <= 0) return true; // sin datos Globex: no penalizar

            double marginTicks = 2.0;
            if (isLong)
                return breakoutPrice > GlobexHigh + (marginTicks * _tickSize);
            else
                return breakoutPrice < GlobexLow - (marginTicks * _tickSize);
        }

        /// <summary>
        /// Actualiza el estado de bloqueo por noticias y la evaluaci n general del contexto.
        /// Llamar en cada barra durante la fase de trading activo.
        /// </summary>
        /// <param name="currentTime">Hora actual de la barra (en ET).</param>
        /// <param name="isRangeValid">Estado de validez del rango ORB.</param>
        /// <param name="isFriday">True si es viernes.</param>
        /// <param name="isDayBeforeHoliday">True si es v spera de festivo USA.</param>
        public void Evaluate(DateTime currentTime, bool isRangeValid,
                             bool isFriday, bool isDayBeforeHoliday)
        {
            // Bloqueo por noticias
            UpdateNewsBlock(currentTime);

            // Viernes despu s de 13:00 ET   no operar
            bool lateFriday = isFriday && currentTime.TimeOfDay >= TimeSpan.FromHours(13);

            IsFavorableContext = isRangeValid
                              && !IsNewsBlocked
                              && VolumeRatio >= 0.60
                              && !lateFriday
                              && !isDayBeforeHoliday;
        }

        /// <summary>
        /// Devuelve el factor de ajuste de tama o por contexto Globex.
        /// 0.70 si el breakout est  dentro del rango Globex, 1.0 si est  afuera.
        /// </summary>
        public double GetGlobexSizeFactor(double breakoutPrice, bool isLong)
        {
            return IsBreakoutOutsideGlobex(breakoutPrice, isLong) ? 1.0 : 0.70;
        }

        #endregion

        #region M todos privados

        /// <summary>
        /// Actualiza IsNewsBlocked y MinutesToNextNews comparando la hora actual
        /// con la lista de horarios de noticias cargada.
        /// </summary>
        private void UpdateNewsBlock(DateTime currentTime)
        {
            double currentHourET = currentTime.Hour + currentTime.Minute / 60.0
                                   + currentTime.Second / 3600.0;

            int minToNews = 999;
            foreach (double newsHour in _newsTimesET)
            {
                double diff = (newsHour - currentHourET) * 60.0; // minutos
                if (diff >= 0 && diff < minToNews)
                    minToNews = (int)Math.Ceiling(diff);
            }

            MinutesToNextNews = minToNews;
            IsNewsBlocked     = minToNews <= _blockMinutesBeforeNews;
        }

        #endregion
    }
}
