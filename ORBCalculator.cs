// ORBCalculator.cs — Construcción y gestión del rango de apertura (ORB)
// Parte del sistema IAOpenRange para NinjaTrader 8
// Independiente: no depende de otros archivos del proyecto

#region Usings
using System;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Construye y gestiona el rango de apertura (Opening Range) de la sesión.
    /// Detecta breakouts, fakeouts y calcula la fuerza del rompimiento.
    /// </summary>
    public class ORBCalculator
    {
        #region Propiedades públicas del rango

        /// <summary>Precio máximo del rango de apertura.</summary>
        public double ORB_High { get; private set; }

        /// <summary>Precio mínimo del rango de apertura.</summary>
        public double ORB_Low { get; private set; }

        /// <summary>Amplitud del rango en ticks.</summary>
        public double ORB_Range { get; private set; }

        /// <summary>Punto medio del rango de apertura.</summary>
        public double ORB_Mid { get; private set; }

        /// <summary>True mientras la ventana ORB está abierta y actualizando.</summary>
        public bool IsRangeBuilding { get; private set; }

        /// <summary>True una vez cerrada la ventana ORB y rango fijado.</summary>
        public bool IsRangeComplete { get; private set; }

        /// <summary>True si el rango cumple los criterios mínimos/máximos de amplitud.</summary>
        public bool IsRangeValid { get; private set; }

        /// <summary>True si se generó señal de compra (cierre sobre ORB_High).</summary>
        public bool HasLongSignal { get; private set; }

        /// <summary>True si se generó señal de venta (cierre bajo ORB_Low).</summary>
        public bool HasShortSignal { get; private set; }

        /// <summary>Número de barra M1 en que se confirmó el breakout alcista.</summary>
        public int LongBreakoutBar { get; private set; }

        /// <summary>Número de barra M1 en que se confirmó el breakout bajista.</summary>
        public int ShortBreakoutBar { get; private set; }

        /// <summary>Distancia en ticks desde el nivel roto al cierre de la barra de breakout.</summary>
        public double BreakoutStrength { get; private set; }

        /// <summary>True si el breakout alcista resultó ser un fakeout.</summary>
        public bool LongFakeout { get; private set; }

        /// <summary>True si el breakout bajista resultó ser un fakeout.</summary>
        public bool ShortFakeout { get; private set; }

        /// <summary>Precio de cierre de la barra de breakout.</summary>
        public double BreakoutBarClose { get; private set; }

        /// <summary>Dirección del último breakout detectado ("LONG", "SHORT" o "NONE").</summary>
        public string LastBreakoutDirection { get; private set; }

        #endregion

        #region Campos privados

        private readonly double _tickSize;
        private readonly int    _minRangeTicks;
        private readonly int    _maxRangeTicks;
        private readonly double _maxRangeVsAtrMultiplier;

        private int    _currentBarIndex;
        private double _dailyAtr14;

        // Seguimiento para detección de fakeout
        private int    _longBreakoutCheckBar;
        private int    _shortBreakoutCheckBar;
        private bool   _longFakeoutChecked;
        private bool   _shortFakeoutChecked;

        // Acción de log (referencia a NinjaScript Print)
        private readonly Action<string> _log;

        #endregion

        #region Constructor

        /// <summary>
        /// Inicializa el calculador de ORB con los parámetros del instrumento.
        /// </summary>
        /// <param name="tickSize">Tamaño del tick del instrumento (ej. 0.25 para ES).</param>
        /// <param name="minRangeTicks">Amplitud mínima válida del rango en ticks.</param>
        /// <param name="maxRangeTicks">Amplitud máxima válida del rango en ticks.</param>
        /// <param name="maxRangeVsAtrMultiplier">Máx ratio rango/ATR (default 1.5).</param>
        /// <param name="log">Delegado para logging (Strategy.Print).</param>
        public ORBCalculator(double tickSize, int minRangeTicks, int maxRangeTicks,
                             double maxRangeVsAtrMultiplier, Action<string> log)
        {
            _tickSize                = tickSize;
            _minRangeTicks           = minRangeTicks;
            _maxRangeTicks           = maxRangeTicks;
            _maxRangeVsAtrMultiplier = maxRangeVsAtrMultiplier;
            _log                     = log ?? (_ => { });
            LastBreakoutDirection    = "NONE";
        }

        #endregion

        #region Métodos públicos

        /// <summary>
        /// Resetea todos los valores al inicio de una nueva sesión.
        /// Llamar desde OnSessionStart() en la estrategia principal.
        /// </summary>
        public void Reset()
        {
            ORB_High              = 0;
            ORB_Low               = double.MaxValue;
            ORB_Range             = 0;
            ORB_Mid               = 0;
            IsRangeBuilding       = false;
            IsRangeComplete       = false;
            IsRangeValid          = false;
            HasLongSignal         = false;
            HasShortSignal        = false;
            LongBreakoutBar       = -1;
            ShortBreakoutBar      = -1;
            BreakoutStrength      = 0;
            LongFakeout           = false;
            ShortFakeout          = false;
            BreakoutBarClose      = 0;
            LastBreakoutDirection = "NONE";
            _longBreakoutCheckBar = -1;
            _shortBreakoutCheckBar= -1;
            _longFakeoutChecked   = false;
            _shortFakeoutChecked  = false;
            _dailyAtr14           = 0;
            _log("[ORBCalc] Rango reseteado para nueva sesión.");
        }

        /// <summary>
        /// Inicia la construcción del rango al abrirse la ventana ORB (09:30 ET).
        /// </summary>
        public void StartBuilding()
        {
            IsRangeBuilding = true;
            ORB_High        = 0;
            ORB_Low         = double.MaxValue;
            _log("[ORBCalc] Inicio de construcción del rango ORB.");
        }

        /// <summary>
        /// Actualiza el rango con cada barra de M1 durante la ventana ORB,
        /// y detecta breakouts una vez que el rango está completo.
        /// </summary>
        /// <param name="high">High de la barra actual M1.</param>
        /// <param name="low">Low de la barra actual M1.</param>
        /// <param name="close">Cierre de la barra actual M1.</param>
        /// <param name="barIndex">Índice de barra actual (CurrentBar).</param>
        /// <param name="dailyAtr14">ATR(14) en ticks del día previo, para validación.</param>
        public void Update(double high, double low, double close, int barIndex, double dailyAtr14)
        {
            _currentBarIndex = barIndex;
            _dailyAtr14      = dailyAtr14;

            if (IsRangeBuilding && !IsRangeComplete)
            {
                // Expandir el rango con cada nueva barra
                if (high > ORB_High) ORB_High = high;
                if (low  < ORB_Low)  ORB_Low  = low;
                return;
            }

            if (!IsRangeComplete || !IsRangeValid) return;

            // Detectar breakout LONG: primera barra que cierra sobre ORB_High
            if (!HasLongSignal && !LongFakeout && close > ORB_High)
            {
                HasLongSignal         = true;
                LongBreakoutBar       = barIndex;
                BreakoutBarClose      = close;
                LastBreakoutDirection = "LONG";
                BreakoutStrength      = Math.Round((close - ORB_High) / _tickSize, 1);
                _log($"[ORBCalc] BREAKOUT LONG detectado — cierre {close:F2}, fuerza {BreakoutStrength} ticks.");
            }

            // Detectar breakout SHORT: primera barra que cierra bajo ORB_Low
            if (!HasShortSignal && !ShortFakeout && close < ORB_Low)
            {
                HasShortSignal        = true;
                ShortBreakoutBar      = barIndex;
                BreakoutBarClose      = close;
                LastBreakoutDirection = "SHORT";
                BreakoutStrength      = Math.Round((ORB_Low - close) / _tickSize, 1);
                _log($"[ORBCalc] BREAKOUT SHORT detectado — cierre {close:F2}, fuerza {BreakoutStrength} ticks.");
            }
        }

        /// <summary>
        /// Cierra la ventana ORB, fija los valores finales y valida el rango.
        /// Llamar exactamente al cierre de la ventana (ej. 10:00 ET).
        /// </summary>
        public void CloseWindow()
        {
            IsRangeBuilding = false;
            IsRangeComplete = true;

            if (ORB_Low >= ORB_High)
            {
                _log("[ORBCalc] ERROR: ORB_Low >= ORB_High al cerrar ventana. Rango inválido.");
                IsRangeValid = false;
                return;
            }

            ORB_Range = Math.Round((ORB_High - ORB_Low) / _tickSize, 1);
            ORB_Mid   = (ORB_High + ORB_Low) / 2.0;

            ValidateRange();

            _log($"[ORBCalc] Ventana cerrada — High:{ORB_High:F2} Low:{ORB_Low:F2} " +
                 $"Range:{ORB_Range}t Mid:{ORB_Mid:F2} Válido:{IsRangeValid}");
        }

        /// <summary>
        /// Verifica si un breakout previo fue un fakeout (retroactivo, 3 barras después).
        /// Llamar en cada barra M1 después del breakout.
        /// </summary>
        /// <param name="close">Cierre de la barra actual.</param>
        /// <param name="barIndex">Índice de barra actual.</param>
        public void CheckFakeout(double close, int barIndex)
        {
            if (!IsRangeComplete) return;

            // Verificar fakeout LONG: 3 barras después del breakout, precio volvió bajo ORB_High
            if (HasLongSignal && !LongFakeout && !_longFakeoutChecked
                && LongBreakoutBar > 0 && barIndex >= LongBreakoutBar + 3)
            {
                _longFakeoutChecked = true;
                if (close <= ORB_High)
                {
                    LongFakeout = true;
                    _log($"[ORBCalc] FAKEOUT LONG confirmado — precio {close:F2} volvió bajo ORB_High {ORB_High:F2}.");
                }
            }

            // Verificar fakeout SHORT: 3 barras después del breakout, precio volvió sobre ORB_Low
            if (HasShortSignal && !ShortFakeout && !_shortFakeoutChecked
                && ShortBreakoutBar > 0 && barIndex >= ShortBreakoutBar + 3)
            {
                _shortFakeoutChecked = true;
                if (close >= ORB_Low)
                {
                    ShortFakeout = true;
                    _log($"[ORBCalc] FAKEOUT SHORT confirmado — precio {close:F2} volvió sobre ORB_Low {ORB_Low:F2}.");
                }
            }
        }

        /// <summary>
        /// Calcula el ratio entre el rango ORB y el ATR diario (0–100+ como porcentaje).
        /// Usado en el payload de la Capa 3 como orb_range_vs_atr_pct.
        /// </summary>
        public double GetRangeVsAtrPct()
        {
            if (_dailyAtr14 <= 0) return 0;
            return Math.Round((ORB_Range / _dailyAtr14) * 100.0, 1);
        }

        /// <summary>
        /// Devuelve true si el precio está actualmente dentro del rango ORB.
        /// Usado para detectar retorno al rango (invalidación del setup en Sección 9).
        /// </summary>
        public bool IsPriceInsideRange(double price)
        {
            return IsRangeComplete && price >= ORB_Low && price <= ORB_High;
        }

        #endregion

        #region Métodos privados

        /// <summary>
        /// Valida que el rango cumple los criterios de amplitud mínima, máxima
        /// y que no sea >= 1.5x el ATR(14) diario previo.
        /// </summary>
        private void ValidateRange()
        {
            if (ORB_Range < _minRangeTicks)
            {
                _log($"[ORBCalc] Rango inválido: {ORB_Range}t < mínimo {_minRangeTicks}t.");
                IsRangeValid = false;
                return;
            }

            if (ORB_Range > _maxRangeTicks)
            {
                _log($"[ORBCalc] Rango inválido: {ORB_Range}t > máximo {_maxRangeTicks}t.");
                IsRangeValid = false;
                return;
            }

            // Verificar ratio vs ATR solo si ATR está disponible
            if (_dailyAtr14 > 0 && ORB_Range >= _dailyAtr14 * _maxRangeVsAtrMultiplier)
            {
                _log($"[ORBCalc] Rango inválido: {ORB_Range}t >= {_maxRangeVsAtrMultiplier}x ATR ({_dailyAtr14}t).");
                IsRangeValid = false;
                return;
            }

            IsRangeValid = true;
        }

        #endregion
    }
}
