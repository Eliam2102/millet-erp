# Runbook — Customización A+W: cola de solicitudes de pedidos para facturación

> **Audiencia:** equipo A+W de Millet (customizings / Gupta) + DevOps Tiglass.
> **Versión:** 1.0.0
> **Última actualización:** 2026-07-05
> **Contexto:** [ADR-0048](../../decisiones/0048-bd-integracion-aw-pedidos.md) ·
> [doc 04 — ingesta de pedidos](../04-ingesta-pedidos-facturacion.md)
> **⚠️ Este flujo NO es el del Glass Agent** (drop service / `last_batch.log` /
> [aw-customizing.md](aw-customizing.md)). Es un flujo nuevo e independiente:
> pedidos en firme → ERP para facturación.

---

## 1. Propósito

El ERP factura los pedidos que A+W libera. Para eso, A+W debe **avisar** al
ERP cada vez que un pedido queda en firme, se modifica o se cancela. El aviso
es un **INSERT** en una tabla de la base `MILLET_INTEGRACION` (misma instancia
SQL Server de AWBUSINESS); el ERP la consume cada pocos minutos, lee los datos
del pedido desde vistas y **escribe de vuelta en la misma fila** el resultado,
y más adelante el UUID fiscal cuando factura.

**A+W no manda los datos del pedido** — solo la señal (número de pedido +
operación + versión). Los datos los lee el ERP de las vistas
`vw_erp_pedido_*`, que consultan las tablas de A+W en read-only.

## 2. La tabla: `MILLET_INTEGRACION.dbo.aw_solicitud_pedido`

DDL completo en
[`02_create_table.sql`](../../operacion/aw-integracion-scripts/02_create_table.sql).
Columnas que escribe A+W:

| Columna | Tipo | Valor |
|---|---|---|
| `numero_pedido` | `nvarchar(50)` | `BW_AUFTR_KOPF.ID` del pedido |
| `operacion` | `tinyint` | `1` = Alta · `2` = Modificación · `3` = Cancelación |
| `version` | `bigint` | Ver §4 — estrictamente creciente por pedido |
| `solicitud_id`, `creada_at` | — | **No enviar** — los pone el default de la tabla |

Las demás columnas (`erp_pedido_id`, `estado_facturacion`, `uuid`,
`resultado`, `motivo`, `procesada_at`) **las escribe el ERP** — ver §5.

### INSERT de referencia

```sql
INSERT INTO MILLET_INTEGRACION.dbo.aw_solicitud_pedido
    (numero_pedido, operacion, version)
VALUES (@numero_pedido, @operacion, @version);
```

## 3. Cuándo insertar cada operación

| Operación | Cuándo | Notas |
|---|---|---|
| **1 — Alta** | El pedido queda **en firme / liberado para facturar** por primera vez | Definir con precisión el evento disparador en A+W (gap G1) |
| **2 — Modificación** | Un pedido ya avisado cambia (cabecera o posiciones) | N veces. El ERP la aplica solo si el pedido aún no tiene factura ("la factura manda"); si ya facturó, responde `Rechazada` y un humano interviene |
| **3 — Cancelación** | El pedido se cancela en A+W | El ERP cancela su pedido interno solo si no tiene CFDI; con CFDI responde `Rechazada` (la cancelación fiscal es un flujo humano SAT 4.0) |

## 3.bis Nudge de baja latencia (opcional pero recomendado)

Sin nudge, el ERP drena la cola cada ~2 minutos (polling). Para latencia de
segundos, **después del INSERT** llamar al exe que el equipo ERP entrega
(instalado en SER-DATA, p.ej. `C:\Millet\aw-pedido-notify\`):

```
MilletAwPedidoNotify.exe <numero_pedido> <alta|modificacion|cancelacion>
```

- Los argumentos son solo para el log local; el exe no manda datos — solo
  "despierta" al ERP (POST con API key, timeout 5s) y sale.
- **Best-effort / fire-and-forget:** si el exe falla (sin internet, ERP
  reiniciando), NO reintentar ni tratar como error — el polling normal
  recoge la solicitud igual. Exit code 0 = avisado, 1 = no avisado (ignorable).
- Nunca llamar al exe ANTES del INSERT (avisaría de nada).

## 4. La columna `version` — regla de oro

- **Estrictamente creciente por `numero_pedido`**, sin reuso ni decremento.
  Sugerencia robusta: `SELECT ISNULL(MAX(version),0)+1 FROM … WHERE
  numero_pedido=@np` dentro de la misma transacción del INSERT.
- Es la defensa de **orden e idempotencia**: el ERP aplica cada solicitud solo
  si su `version` es mayor a la última aplicada.
- **Recomendado (blueprint del diseño JSON 2026-05):** mantener una tabla
  historial propia con detección de cambios reales — si el pedido no cambió
  desde el último aviso, **no insertar** (evita solicitudes ruidosas). El
  mecanismo `version_pedido` + `HISTORIAL_INTEGRACION` que se diseñó para el
  JSON aplica idéntico aquí.

## 5. Qué puede leer A+W de vuelta

En la misma fila, escritas por el ERP:

| Columna | Cuándo la escribe el ERP | Significado |
|---|---|---|
| `erp_pedido_id` | Al procesar la solicitud | Claim de correlación. **Nunca se borra** |
| `resultado` / `motivo` / `procesada_at` | Al procesar la solicitud | `1` Aplicada · `2` Rechazada (+motivo) · `3` Pospuesta (se reintenta sola) · `4` Error |
| `estado_facturacion` | Al procesar y al facturar/cancelar | `SinFacturar` → `Facturado` / `Cancelado` |
| `uuid` | Al timbrar el CFDI | Folio fiscal (36 chars, con guiones) |

## 6. Prohibiciones

1. **No UPDATE** de filas ya insertadas (ni de sus propias columnas): una
   corrección = nueva solicitud con `version` mayor.
2. **No DELETE**: la tabla es bitácora de correlación; el claim del ERP
   persiste siempre.
3. **No escribir** en las columnas del ERP (`erp_pedido_id`,
   `estado_facturacion`, `uuid`, `resultado`, `motivo`, `procesada_at`).
4. **Solo la BD de producción** (`MILLET_INTEGRACION`): la customización jamás
   apunta a `MILLET_INTEGRACION_DEV` (esa la llena el equipo ERP con seeds).

## 7. Gaps a confirmar (equipo A+W ⇄ equipo ERP)

| # | Pregunta | Impacto |
|---|---|---|
| G1 | ¿Qué evento exacto de A+W marca "en firme" y dónde se engancha el INSERT (trigger, job, hook Gupta)? | Diseño de la customización |
| G2 | ¿Pueden garantizar `version` monotónica por pedido (ideal: con detección de cambios §4)? | Idempotencia |
| G3 | Colación/encoding de `MILMAIN` (acentos en nombres/notas) | DDL de `MILLET_INTEGRACION` |
| G5 | ¿De dónde saldrá `requiere_pedimento` por posición? (el diseño JSON lo dejó "pendiente definir origen") | Facturas de exportación 29-A |
| G6 | Valores reales de `canal_ventas` / `clase` / `numero_sucursal` / estatus del pedido | Mapeos de configuración del ERP |
| G7 | Volumen esperado (pedidos/día y modificaciones/pedido) | Tuning del worker (batch/intervalo) |
| G8 | Login SQL para la customización (INSERT en la tabla) y visto bueno al usuario `millet_erp_integracion` (SELECT sobre `MILMAIN`) | Permisos |
| G9 | ¿A+W espera también el estado `Liquidado` (estatus 70, pago aplicado) o basta `Facturado`/`Cancelado`? | Alcance del write-back REPP |
| G10 | Proceso operativo cuando el ERP responda `Rechazada` sobre un pedido ya facturado (modificación/cancelación post-factura) | Procedimiento humano |
| G-wb | Write-back de `uuid`/`estado_facturacion`: ¿en la fila de la última `version` o en todas las filas del pedido? | Query de la customización al leer |
| G11 | Confirmar el INSERT `operacion=3` para cancelaciones (en el diseño JSON quedó "pendiente de coordinación") | Cobertura de cancelación |
| G12 | ¿KU_KUNDEN tiene régimen fiscal / CP fiscal? Si no, se completan en el ERP antes de timbrar | Auto-provisión de clientes |
| G13 | ¿El usuario del ERP puede ejecutar `SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N` / `RtfToText` vía las vistas? ¿Perf aceptable en SELECT por pedido? | Vistas de líneas/notas |

## 8. Validación (smoke test con el equipo ERP)

1. Equipo ERP inserta a mano una solicitud de un pedido real en
   `MILLET_INTEGRACION_DEV` y verifica el ciclo completo (claim + resultado).
2. Equipo A+W activa la customización en QA/espejo (si existe) o en una
   ventana controlada: liberar un pedido de prueba → verificar INSERT.
3. Verificar juntos el write-back: el ERP marca `Aplicada` y, tras una factura
   de prueba, el `uuid` aparece en la fila.
