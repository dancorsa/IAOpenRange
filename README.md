# IAOpenRange — Sistema ORB con IA Multicapa para NinjaTrader 8

Estrategia de **Opening Range Breakout** con 5 capas de inteligencia artificial para futuros CME (ES, NQ, RTY, CL, GC y versiones Micro).

---

## Arquitectura del sistema

```
OpeningRangeBreakoutAI.cs   ← Estrategia principal (loop NT8)
    │
    ├─ ORBCalculator.cs      ← Detecta y valida el rango de apertura
    ├─ ORBContextFilter.cs   ← Gap, Globex, volumen, sesgo del día
    ├─ ORBRiskManager.cs     ← Tamaño de posición, stops, targets
    ├─ ORBTradeJournal.cs    ← Historial persistente de trades
    └─ ORBAIOrchestrator.cs  ← Motor de IA (5 capas)
            │
            ├─ Capa 1: Análisis de régimen diario   (OnSessionStart, async)
            ├─ Capa 2: Aprendizaje continuo          (OnSessionStart, async)
            ├─ Capa 3: Validación de entrada         (bloqueante, <8s timeout)
            ├─ Capa 4: Guardia de riesgo sistémico   (bloqueante, posición abierta)
            └─ Capa 5: Análisis post-trade           (fire-and-forget)
```

**Proveedores soportados:** Claude (Anthropic) · OpenAI · Disabled (sin IA)  
**Enum:** `ORBAIProvider { Claude, OpenAI, Disabled }` — aislado del enum `AIProvider` del sistema MeanReversionAI.

---

## Restricciones de plataforma (.NET 4.8 / NinjaTrader 8)

Estos puntos son críticos al desarrollar con cualquier herramienta de IA:

| Restricción | Detalle |
|---|---|
| Sin `System.Text.Json` nativo | El archivo incluye un polyfill propio dentro de `namespace System.Text.Json`. No agregar NuGet externo. |
| Sin `Newtonsoft.Json` explícito | NT8 lo carga pero los imports en NinjaScript a veces fallan. Usar el polyfill integrado. |
| `MACD.Histogram` no existe | Usar `MACD.Diff[0]` para el histograma en NT8 |
| `TradesPerformance.TotalProfit` no existe | Usar `GetCumProfit() - _cumProfitAtEntry` (helper local) |
| `IsSuspendedWhileInactive` solo en Indicators | No disponible en la clase Strategy |
| VWAP sin tipo explícito | Usar `_sessionVwap` (calculado manualmente como TP/Vol acumulado) |
| `[Range]` y `[Display]` | Requieren `using System.ComponentModel.DataAnnotations;` |
| `Brushes.*` | Requieren `using System.Windows.Media;` |
| `DashStyleHelper` | Requiere `using NinjaTrader.Gui;` |
| `OnSessionStart()` override | Válido en Strategy, pero solo disponible en live/paper (State != Historical) |

---

## Flujo de trabajo

```
GitHub (editar)  →  copiar a NT8  →  compilar en NinjaTrader  →  validar errores
```

1. **Editar siempre en** `GitHub/IAOpenRange/` (control de versiones).
2. **Copiar el archivo modificado** a `NinjaTrader 8/bin/Custom/Strategies/`.
3. **Compilar en NT8:** menú NinjaScript Editor → Compile.
4. Si hay errores, corregir en GitHub, volver al paso 2.

> **Nunca editar directamente en la carpeta de NinjaTrader** — se pierden los cambios al hacer sync desde GitHub.

---

## Guía para trabajar con IAs de desarrollo

### Contexto que siempre debes dar al AI

Cuando abras una sesión con Claude Code, Codex u otra IA, proporciona este contexto:

```
Proyecto: IAOpenRange — NinjaScript para NinjaTrader 8, .NET 4.8, C# 7.x
Restricciones:
- NO usar System.Text.Json (hay polyfill propio en ORBAIOrchestrator.cs)
- NO usar Newtonsoft.Json directamente
- MACD histograma: .Diff[0] (no .Histogram)
- VWAP: variable _sessionVwap calculada manualmente
- Enums: ORBAIProvider (no AIProvider) para la estrategia ORB
- El MeanReversionAI usa su propio AIProvider (no tocar)
- Targets de profit: SetProfitTarget con nombre de orden específico
- Stops: SetStopLoss con nombre de orden específico
```

### Con Claude Code (este proyecto)

Claude Code tiene acceso directo a los archivos. Flujo recomendado:

1. Abrir sesión desde `GitHub/IAOpenRange/` como directorio de trabajo.
2. Pedir cambios descriptivos: *"Agrega filtro de volumen en ConditionsMetForLong"*.
3. Claude edita el archivo en GitHub directamente.
4. Copiar manualmente a NT8 y compilar.
5. Si hay errores, pegar el CSV de errores de NT8 en el chat → Claude los corrige.

**Adjuntar errores:** exportar desde el NT8 Output window como CSV o copiar texto, luego pegar en el chat. Claude los analiza todos de una vez.

### Con OpenAI Codex / ChatGPT

Codex no tiene acceso a los archivos. Flujo recomendado:

1. Copiar el archivo completo en el prompt (o la sección relevante).
2. Incluir el bloque de contexto de arriba.
3. Pedir el cambio específico.
4. Copiar la respuesta, reemplazar el archivo en GitHub.
5. Compilar en NT8 y validar.

**Limitación:** Codex no ve el historial de cambios ni los otros archivos del proyecto. Siempre proporcionar el archivo completo o la sección relevante.

### Qué pedir a cada IA

| Tarea | Mejor herramienta |
|---|---|
| Corregir errores de compilación NT8 | Claude Code (puede leer todos los archivos) |
| Nuevo filtro o condición de entrada | Cualquiera (dar contexto) |
| Refactoring de lógica compleja | Claude Code |
| Snippet rápido de código NT8 | Codex / ChatGPT |
| Debugging de lógica de trading | Claude Code (entiende el contexto completo) |
| Generar variantes de strategy para probar | Codex (rápido para generar alternativas) |

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `OpeningRangeBreakoutAI.cs` | Estrategia principal. Loop NT8, eventos, visualización |
| `ORBAIOrchestrator.cs` | Motor HTTP para Claude/OpenAI. Define `ORBAIProvider` y las 5 capas |
| `ORBCalculator.cs` | Detecta High/Low del rango, breakouts, fakeouts |
| `ORBContextFilter.cs` | Gap de apertura, Globex, volumen relativo, sesgo del día |
| `ORBRiskManager.cs` | Contratos, stops, targets, límites diarios |
| `ORBTradeJournal.cs` | Persiste historial de trades en disco |

---

## Convenciones del código

- **Nombres de órdenes**: `"ORB_LONG"`, `"ORB_SHORT"` — deben coincidir entre `EnterLong/Short`, `SetStopLoss` y `SetProfitTarget`.
- **BarsInProgress**: `0` = M1 (primaria), `1` = M5, `2` = M15.
- **Async en NT8**: usar `Task.Run(async () => { ... })` para fire-and-forget. No `await` directo en `OnBarUpdate`.
- **Llamadas bloqueantes**: `Task.Run(() => ...).GetAwaiter().GetResult()` para Capas 3 y 4.
- **JSON polyfill**: `JsonSerializer.Serialize(new { snake_case_field = value })` — los nombres de campo anónimos se usan tal cual en el JSON.

---

## Errores frecuentes y sus causas

| Error NT8 | Causa | Fix |
|---|---|---|
| `CS0234 System.Text.Json` | Se eliminó el polyfill del archivo | Restaurar el bloque `namespace System.Text.Json { ... }` al inicio de ORBAIOrchestrator.cs |
| `CS0101 AIProvider duplicado` | Dos archivos definen el mismo enum en el mismo namespace | ORB usa `ORBAIProvider`, MR usa `AIProvider` — no mezclar |
| `CS1061 Histogram` | Propiedad incorrecta de MACD en NT8 | Cambiar a `.Diff[0]` |
| `CS0246 Range/Display` | Falta using | Agregar `using System.ComponentModel.DataAnnotations;` |
| `CS0246 Brushes` | Falta using | Agregar `using System.Windows.Media;` |
| `CS0115 OnSessionStart` | Cascada de otros errores | Se resuelve al corregir los errores primarios |
| `CS0103 VWAP` | Tipo VWAP no disponible en esta versión NT8 | Usar `_sessionVwap` (calculado en `UpdateSessionVwap()`) |
