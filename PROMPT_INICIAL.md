# Prompt Inicial — IAOpenRange

> Texto original con el que se solicitó el desarrollo del sistema. Conservar como referencia histórica.

---

Eres un experto en desarrollo de estrategias de trading algorítmico para NinjaTrader 8 
usando C# y NinjaScript. Tu tarea es construir una estrategia completa e independiente 
de OPENING RANGE BREAKOUT (ORB) para futuros (ES, NQ, RTY, CL, GC y sus versiones Micro), 
con un sistema de IA MULTICAPA (no solo validación de entrada), gestión de riesgo 
dinámica y aprendizaje continuo entre sesiones.

Esta estrategia debe funcionar de forma totalmente autónoma, sin depender 
de ningún otro archivo externo al proyecto.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## ARQUITECTURA — 6 ARCHIVOS COMPLETOS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  1. OpeningRangeBreakoutAI.cs   → Estrategia principal (Strategy)
  2. ORBCalculator.cs            → Construcción y gestión del rango de apertura
  3. ORBContextFilter.cs         → Filtros de contexto: gap, Globex, volumen, noticias
  4. ORBRiskManager.cs           → Gestión de riesgo, stops y targets
  5. ORBAIOrchestrator.cs        → Orquestador de las 5 capas de IA (NUEVO - núcleo)
  6. ORBTradeJournal.cs          → Historial de trades para aprendizaje continuo (NUEVO)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 1. PRINCIPIO CENTRAL DE LA ESTRATEGIA
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

El Opening Range Breakout se basa en que los primeros N minutos de la 
sesión regular establecen un rango de precio (High y Low) que representa 
el equilibrio inicial del mercado. Cuando el precio rompe ese rango con 
convicción, suele continuar en esa dirección por inercia institucional.

A diferencia de un ORB tradicional, este sistema usa IA en 5 momentos 
distintos del ciclo de vida del trade, no solo al validar la entrada:

  1. Al INICIO del día → análisis de régimen (¿qué tipo de jornada es hoy?)
  2. Al INICIO del día → aprendizaje de trades pasados (¿qué está funcionando?)
  3. Al GENERAR una señal → validación de entrada (la capa tradicional)
  4. DURANTE la posición → guardia de riesgo sistémico (eventos anómalos)
  5. Al CERRAR el trade → análisis post-trade (¿por qué ganó o perdió?)

El sistema solo opera cuando todas las capas relevantes son favorables.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 2. TEMPORALIDAD Y FASES DEL DÍA
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Chart principal:  M1  → construcción precisa del rango y señal de entrada
Chart contexto:   M5  → confirmación de breakout, momentum y guardia de riesgo
Chart macro:      M15 → tendencia del día y niveles clave

Usar AddDataSeries() de NinjaTrader para los 3 timeframes.

### FASE 0 — Pre-apertura del sistema (antes de 08:00 ET, una vez por sesión)

  - Llamar a CAPA IA #1 (régimen diario) y CAPA IA #2 (aprendizaje continuo)
  - Ambas llamadas ocurren en OnSessionStart(), de forma asíncrona,
    ANTES de que comience la construcción del rango
  - Si la respuesta de régimen indica "stay_flat" → desactivar la estrategia 
    para todo el día (propiedad `_dailyTradingEnabled = false`)
  - Guardar el resultado en propiedades de la clase para uso durante el día

### FASE 1 — Pre-market (08:00–09:30 ET)

  - Calcular el High y Low de la sesión Globex overnight
  - Calcular el Gap respecto al cierre anterior:
      GapPct = (Open_actual - Close_previo) / Close_previo * 100
  - Identificar niveles clave: VWAP overnight, POC del día previo
  - Calcular el ATR(14) pre-market como referencia de volatilidad
  - Almacenar estos valores en propiedades de ORBContextFilter

### FASE 2 — Ventana ORB (09:30–10:00 ET, configurable)

  - Registrar el primer tick de 09:30:00 ET exactamente
  - Actualizar ORB_High y ORB_Low con cada nuevo High/Low de M1
  - Al cierre de la ventana: fijar ORB_High, ORB_Low, ORB_Range, ORB_Mid

Parámetro editable: `ORBWindowMinutes` (default 30, opciones: 5, 15, 30, 60)

Validación del rango:
  - ORB_Range >= `MinRangeTicks` (default: 8 ES, 15 NQ)
  - ORB_Range <= `MaxRangeTicks` (default: 60 ES, 100 NQ)
  - Verificar que el rango no sea >= 1.5x el ATR(14) diario previo
  - Exponer propiedad: `IsRangeValid` (bool)

### FASE 3 — Zona de breakout activa (10:00–14:30 ET)

  - Búsqueda y ejecución de señales de breakout
  - CAPA IA #3 (validación de entrada) se activa aquí
  - CAPA IA #4 (guardia de riesgo) corre en paralelo cada 5 barras de M5
    si hay posición abierta

### FASE 4 — Cierre forzado (14:30–15:15 ET)

  - 14:30: no abrir nuevas posiciones
  - Si hay posición abierta: mover stop a breakeven si aún no está
  - 15:00: cerrar toda posición a mercado sin excepción
  - Al cerrar cualquier trade (en cualquier momento del día): 
    activar CAPA IA #5 (análisis post-trade) de forma asíncrona
  - 15:15: resetear ORBCalculator y guardar resumen del día en ORBTradeJournal

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 3. ORBCalculator.cs — CONSTRUCCIÓN DEL RANGO
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Clase helper independiente. Instanciar desde OpeningRangeBreakoutAI.cs.

### 3.1 Propiedades públicas

  double ORB_High, ORB_Low, ORB_Range, ORB_Mid
  bool   IsRangeBuilding, IsRangeComplete, IsRangeValid
  bool   HasLongSignal, HasShortSignal
  int    LongBreakoutBar, ShortBreakoutBar
  double BreakoutStrength

### 3.2 Método Reset()

Llamar en OnSessionStart(): resetear todas las propiedades.

### 3.3 Método Update(double high, double low, double close, long volume, DateTime time)

Llamar en cada barra de M1:
  - Durante ventana ORB: actualizar High/Low del rango
  - Al cierre de ventana: fijar valores finales y validar amplitud
  - Detectar breakout: close > ORB_High (LONG) o close < ORB_Low (SHORT)
  - Calcular BreakoutStrength en ticks de distancia al nivel roto

### 3.4 Detección de fakeout (retroactivo)

Método `CheckFakeout()`:
  - Si HasLongSignal y 3 barras después el precio cerró bajo ORB_High → fakeout
  - Si HasShortSignal y 3 barras después el precio cerró sobre ORB_Low → fakeout
  - Marcar `_longFakeout` o `_shortFakeout` para no repetir señal en esa dirección

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 4. ORBContextFilter.cs — FILTROS DE CONTEXTO
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

### 4.1 Análisis de Gap

  GapPct = ((Open_actual - Close_previo) / Close_previo) * 100
  GapPct > +0.30%  → GapDirection = "UP"
  GapPct < -0.30%  → GapDirection = "DOWN"
  Abs(GapPct) <= 0.30% → GapDirection = "FLAT"

Parámetro editable: `GapThresholdPct` (default 0.30)

### 4.2 Contexto Globex

  GlobexHigh, GlobexLow, GlobexRange (en ticks)
  Si ORB_High > GlobexHigh + 2 ticks → breakout LONG es expansión real
  Si ORB_Low < GlobexLow - 2 ticks → breakout SHORT es expansión real
  Si el breakout está DENTRO del rango Globex → señal más débil, tamaño 70%

### 4.3 Filtro de volumen de apertura

  VolumeRatio = Volumen_primera_hora / VolumenPromedio_30dias
  >= 1.20 → alta confiabilidad
  0.80–1.20 → normal
  < 0.80 → reducir tamaño al 60%

### 4.4 Tendencia del día (pre-clasificación)

  Usando M15 desde las 09:30: comparar contra VWAP del día previo
  Exponer: `DayBias` (enum: Bullish / Bearish / Neutral)

### 4.5 Filtro de noticias de alto impacto

Parámetro: `BlockMinutesBeforeNews` (default 15)
Lista editable: `HighImpactTimes` (string CSV, ej: "08:30,10:00,14:00")

### 4.6 Propiedad pública IsFavorableContext

True si: IsRangeValid, VolumeRatio >= 0.60, sin noticia próxima, rango no anómalo.
False si: viernes después de 13:00 ET, víspera de festivo USA.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 5. REGLAS DE ENTRADA LONG Y SHORT
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

### ENTRADA LONG — todas las condiciones deben cumplirse:

CONDICIÓN 1 — Sistema y contexto:
  - `_dailyTradingEnabled` == true
  - ORBCalculator.IsRangeValid == true
  - ORBContextFilter.IsFavorableContext == true
  - Hora actual entre 10:00 ET y 14:30 ET
  - `_longFakeout` == false

CONDICIÓN 2 — Breakout confirmado en M1:
  - Cierre POR ENCIMA de ORB_High, más de `BreakoutConfirmTicks` ticks (default 2)
  - Es la primera barra que cierra sobre ORB_High
  - Dentro de `MaxBarsAfterBreakout` barras (default 3)

CONDICIÓN 3 — Confirmación de volumen y momentum:
  - Volumen > promedio_20_barras * 1.30
  - RSI(14) en M5 > 50 y creciendo
  - MACD histograma positivo en M5

CONDICIÓN 4 — Sin resistencia inmediata:
  - No hay resistencia fuerte dentro de `ClearanceZoneTicks` ticks (default 10)
  - Penalizar tamaño si está dentro del rango Globex

CONDICIÓN 5 — Alineación con contexto del día:
  - Si GapDirection == "DOWN" → no operar LONG salvo `TradeAgainstGap` == true
  - Si DayBias == Bearish → reducir tamaño al 70%
  - Si CAPA IA #1 devolvió `avoid_directions` incluyendo "LONG" → bloquear

### ENTRADA SHORT — condiciones espejo inversas

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 6. ORBAIOrchestrator.cs — NÚCLEO DE LAS 5 CAPAS DE IA
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

HttpClient instanciado UNA SOLA VEZ en el constructor.
Todas las llamadas son async/await. Timeout 3 segundos con fallback graceful.
Las Capas 1, 2 y 5 NO bloquean el flujo principal.
Las Capas 3 y 4 SÍ son bloqueantes.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
### 6.1 CAPA IA #1 — Análisis de régimen diario

Método: `Task<RegimeAnalysis> AnalyzeDailyRegimeAsync(DailyContextData data)`

Respuesta esperada JSON:
{
  "regime": "trending / ranging / high_volatility / uncertain",
  "conviction": 0.0-1.0,
  "favorable_for_orb": true/false,
  "max_risk_today": 0.5-1.0,
  "avoid_directions": [],
  "regime_reason": "string max 150 chars"
}

Si favorable_for_orb == false AND conviction >= 0.70 → desactivar día.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
### 6.2 CAPA IA #2 — Aprendizaje continuo

Método: `Task<LearningAdjustment> AnalyzeRecentPerformanceAsync(List<TradeRecord> last20Trades)`

Respuesta esperada JSON:
{
  "adjusted_min_confidence": 0.55-0.85,
  "patterns_working": ["string"],
  "patterns_failing": ["string"],
  "session_guidance": "string max 200 chars"
}

adjusted_min_confidence sobreescribe AIMinConfidence para el día.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
### 6.3 CAPA IA #3 — Validación de entrada (bloqueante)

Método: `Task<EntrySignalValidation> ValidateEntryAsync(ORBSignalPayload payload)`

Respuesta esperada JSON:
{
  "approve": true/false,
  "confidence": 0.0-1.0,
  "reason": "string max 120 chars",
  "risk_adjustment": 0.5-1.0,
  "fakeout_probability": 0.0-1.0
}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
### 6.4 CAPA IA #4 — Guardia de riesgo sistémico (bloqueante)

Activar solo cuando: barra > 2x ATR en M5, o volumen > 3x promedio.
Verificar cada `RiskGuardCheckIntervalBars` barras de M5.

Método: `Task<RiskGuardAction> CheckSystemicRiskAsync(RiskGuardPayload payload)`

Respuesta esperada JSON:
{
  "action": "hold / tighten_stop / close_immediately",
  "urgency": "low / medium / high / critical",
  "new_stop_distance_ticks": [valor o null],
  "reasoning": "string max 100 chars"
}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
### 6.5 CAPA IA #5 — Análisis post-trade (fire-and-forget)

Método: `Task<PostTradeAnalysis> AnalyzeClosedTradeAsync(ClosedTradePayload payload)`

Respuesta esperada JSON:
{
  "primary_failure_reason": "string o null",
  "pattern_tag": "fakeout_on_gap_against / low_volume_entry / news_spike / 
                  clean_breakout / momentum_exhaustion / range_day / time_stop",
  "confidence_calibration_error": 0.0-1.0,
  "lesson": "string max 150 chars"
}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
### 6.6 Configuración de proveedor (compartida por las 5 capas)

OPCIÓN A — Claude (Anthropic):
  URL: https://api.anthropic.com/v1/messages
  Headers: "x-api-key": {APIKey}, "anthropic-version": "2023-06-01"
  Model: "claude-sonnet-4-6"

OPCIÓN B — OpenAI (DEFAULT):
  URL: https://api.openai.com/v1/chat/completions
  Headers: "Authorization": "Bearer {APIKey}"
  Model: "gpt-4o"

Parámetros editables:
  AIProvider (enum: Claude / OpenAI / Disabled)
  AIApiKey, AIMinConfidence (default 0.65), FakeoutMaxThreshold (default 0.50)
  EnableAIValidation, EnableDailyRegimeCheck, EnableLearningLayer,
  EnableRiskGuard, RiskGuardCheckIntervalBars (default 5),
  EnablePostTradeAnalysis, JournalLookbackTrades (default 20)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 7. ORBTradeJournal.cs — HISTORIAL PARA APRENDIZAJE

Persistencia CSV: Documents\NinjaTrader 8\journal\ORB_TradeJournal_{instrumento}.csv

Campos: Date, Direction, AiConfidenceAtEntry, AiFakeoutProb, Result, RMultiple,
ExitReason, DayOfWeek, GapDirection, VolumeRatio, OrbRangeTicks, PatternTag

Métodos: AddTrade(), GetLast(n), LoadFromDisk(), SaveToDisk(), GetSummary(days)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 8. ORBRiskManager.cs — GESTIÓN DE RIESGO

Stop = MAX(ORB_Range × StopRangeMultiplier, ATR × StopATRMultiplier, MinStopTicks)

Contratos = Floor( (Capital × RiskPct × MaxRiskTodayMultiplier) / (StopTicks × ValorTick) )

Multiplicadores: Globex 0.70, Volumen bajo 0.60, DayBias opuesto 0.70,
AI risk_adjustment, Capa 1 max_risk_today.

TP1 = 1.5× rango (40% posición) → breakeven + trailing
TP2 = 2.5× rango (35% posición)
TP3 = 4.0× rango (25% posición)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 9. SEÑALES DE SALIDA

TP1/TP2/TP3 → parciales
Precio regresa al rango ORB por 3 barras → invalidación
CAPA IA #4 close_immediately → emergencia
14:30 → breakeven; 15:00 → cierre forzado total

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 10. VISUALIZACIÓN

- Líneas ORB_High (verde), ORB_Low (rojo), ORB_Mid (gris punteado)
- Rectángulo semitransparente del rango
- Flechas en entradas aprobadas, "X" en rechazadas
- Panel de texto: régimen, WR 7d, confianza ajustada, trades del día

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 11. LOGGING Y MONITOREO

CSV diario con todas las capas:
  timestamp_entry/exit, instrument, direction, entry/exit price, contracts,
  pnl_ticks, pnl_usd, orb_range, gap, globex, volume_ratio, day_bias,
  rsi_m5, macd_hist_m5, clearance_ticks, bars_since_breakout, was_reentry,
  daily_regime, regime_conviction, max_risk_today_applied,
  session_adjusted_confidence, patterns_failing_flagged,
  ai_confidence, ai_risk_adjustment, ai_fakeout_probability, ai_reason,
  risk_guard_triggered, risk_guard_action,
  post_trade_pattern_tag, confidence_calibration_error, post_trade_lesson,
  stop_ticks, tp1_hit, tp2_hit, tp3_hit, exit_reason, consecutive_losses_at_entry

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 12. PARÁMETROS EDITABLES — RESUMEN COMPLETO

CATEGORÍA "ORB Setup":
  ORBWindowMinutes, MinRangeTicks, MaxRangeTicks, BreakoutConfirmTicks,
  MaxBarsAfterBreakout, ClearanceZoneTicks, TradeAgainstGap,
  AllowReEntry, ReEntryWaitBars, AllowReversalTrade

CATEGORÍA "Sesión":
  TradingStartTime, TradingEndTime, ForceCloseTime, GapThresholdPct,
  BlockMinutesBeforeNews, HighImpactTimes

CATEGORÍA "Risk Management":
  RiskPctPerTrade, MaxContracts, StopRangeMultiplier, StopATRMultiplier,
  MinStopTicks, TrailingATRMultiplier, TP1_Multiplier, TP2_Multiplier,
  TP3_Multiplier, TP1_ClosePct, TP2_ClosePct, TP3_ClosePct,
  MaxDailyLoss, MaxDailyProfit, MaxDailyTrades, MaxConsecutiveLosses

CATEGORÍA "AI Core (Capa 3)":
  AIProvider, AIApiKey, AIMinConfidence, FakeoutMaxThreshold, EnableAIValidation

CATEGORÍA "AI Advanced Layers":
  EnableDailyRegimeCheck, EnableLearningLayer, EnableRiskGuard,
  RiskGuardCheckIntervalBars, EnablePostTradeAnalysis, JournalLookbackTrades

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 13. REQUISITOS TÉCNICOS NINJASCRIPT

- NinjaTrader 8, .NET Framework 4.8, C# 7.x
- AddDataSeries() para M5 y M15
- Capas 1, 2 y 5 deshabilitadas en State.Historical (no hacer llamadas HTTP en backtest)
- HttpClient instanciado en State.Configure, disposed en State.Terminated
- Sin NuGet packages adicionales
- Comentarios en español, XML en métodos públicos
- debe soportar configuración para chatgpt por defecto pero también para los modelos de claude

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
## 14. ENTREGABLES SOLICITADOS

1. ORBCalculator.cs
2. ORBContextFilter.cs
3. ORBTradeJournal.cs
4. ORBAIOrchestrator.cs
5. ORBRiskManager.cs
6. OpeningRangeBreakoutAI.cs
7. Configuración recomendada para ES (ventana 30 min, capas activas)
8. Configuración recomendada para NQ (ventana 15 min)
9. Tabla de ajuste de MinRangeTicks/MaxRangeTicks por instrumento
10. Diagrama de secuencia ASCII de las 5 capas durante un día típico
11. Checklist antes de live trading

Orden de construcción: ORBCalculator → ORBContextFilter → ORBTradeJournal
→ ORBAIOrchestrator → ORBRiskManager → OpeningRangeBreakoutAI

---

## Estado de implementación vs prompt

| Entregable | Estado | Notas |
|---|---|---|
| 1. ORBCalculator.cs | ✅ Completo | 321 líneas, todas las funciones implementadas |
| 2. ORBContextFilter.cs | ✅ Completo | 291 líneas, gap/Globex/volumen/sesgo/noticias |
| 3. ORBTradeJournal.cs | ✅ Completo | 340 líneas, CSV persistente, estadísticas |
| 4. ORBAIOrchestrator.cs | ✅ Completo | 1007 líneas, 5 capas + JSON polyfill |
| 5. ORBRiskManager.cs | ✅ Completo | 351 líneas, sizing/stops/targets/límites |
| 6. OpeningRangeBreakoutAI.cs | ✅ Completo | 1251 líneas, integración total |
| 7. Config recomendada ES | ⏳ Pendiente | No documentada en archivos del proyecto |
| 8. Config recomendada NQ | ⏳ Pendiente | No documentada |
| 9. Tabla ticks por instrumento | ⏳ Pendiente | No documentada |
| 10. Diagrama de secuencia ASCII | ⏳ Pendiente | No generado |
| 11. Checklist live trading | ⏳ Pendiente | No generado |
| `AllowReversalTrade` (param) | ⚠️ Revisar | Listado en sección 12 del prompt, verificar si está en el código |
| `TP1_ClosePct`/`TP2_ClosePct`/`TP3_ClosePct` | ⚠️ Revisar | Listados en sección 12, verificar implementación |
| Log CSV diario (sección 11) | ⚠️ Parcial | Journal existe; el CSV de auditoría completo con todas las columnas de la sección 11 puede no estar separado del journal |
| Timeout 3 seg (sección 6) | ⚠️ Nota | Implementado como 8 segundos (ajuste posterior) |
