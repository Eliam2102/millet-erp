## ADR-0033: Setting `AutoGenerarOcAlAutorizar` por empresa

- **Estado**: Aceptada
- **Fecha**: 2026-05-13
- **Decisores**: Eduardo Paredes (owner), Claude (backend/frontend)
- **Etiquetas**: compras, requisiciones, ordenes-compra, configuracion, settings

## Contexto y problema

El diseño de Requisiciones (RQ) asunción **A3** quedó como *"Generación de OC y movimiento de almacén: automática y síncrona dentro del handler de `AutorizarRequisicionCommand`. No hay estado intermedio 'autorizada pero sin OC'"* — cerrada con el cliente desde 2026-05-09.

El diseño de Órdenes de Compra (OC) §1.2 y §3.bis.1 — escrito después — describió flujos donde **el comprador dispara la creación de OC manualmente**:

- "1:1 desde requisición autorizada" (bandeja de RQ → "convertir en OC").
- "N:1 consolidación" (selector de RQs en el Sheet "Nueva OC").

Filtros del selector: `r.estado = 'Autorizada' AND r.comprometida_en_oc_id IS NULL`.

**Las dos narrativas son incompatibles**:

- Si el handler de autorizar genera OC automática (A3), la RQ siempre nace comprometida o pasa a `EnSurtido` con la OC borrador stub asociada. El selector "1:1 / N:1" del diseño OC nunca tiene candidatos disponibles.
- Si el comprador convierte manualmente (OC §3.bis.1), el handler de autorizar NO debe llamar al puerto OC. La RQ queda esperando conversión.

El código en `main` al 2026-05-13 implementaba una **mezcla incoherente**: el handler de autorizar llamaba al puerto stub (Narrativa A) y dejaba la RQ en `EnSurtido` sin setear `ComprometidaEnOcId`. El selector del Sheet "Nueva OC" filtraba por `Estado=Autorizada` (Narrativa B) y nunca encontraba candidatos. Resultado: RQs atrapadas en `EnSurtido` sin camino limpio a OC. Reproducción: `compras.oc_borrador_stub` contenía filas huérfanas que ningún flujo procesaba.

El owner solicitó conservar AMBAS narrativas y resolverlas vía **setting configurable por empresa**.

## Drivers de la decisión

- Encajar con dos modelos de operación distintos según el cliente: algunas empresas prefieren que el sistema genere OC borradores automáticamente al autorizar (workflow histórico SAP); otras prefieren control manual del comprador (workflow nuevo, alineado con consolidación N:1).
- Default que coincida con el modelo mental del owner ("nada es automático, el user debe convertir a OC o crear OC consolidada").
- Reversible: el setting es por empresa y se cambia con un PATCH endpoint sin redeploy.
- Compatibilidad regresiva: tests existentes que dependían del puerto OC stub siguen funcionando.

## Opciones consideradas

1. **Setting por empresa con default false (manual)** — *elegida*.
2. **Setting por empresa con default true (automático)** — preserva el comportamiento histórico, pero no encaja con el modelo mental del owner.
3. **Hardcode una narrativa** — descarta la otra. Obliga a re-hacer ADR cuando un cliente pida la opuesta.
4. **Setting global vía `appsettings.json`** — simple pero no soporta multi-tenant. Si en el futuro entran 2+ empresas con preferencias distintas, hay que migrar al modelo por empresa.

## Decisión

**Setting `AutoGenerarOcAlAutorizar : bool` persistido en `compras.settings`, una fila por empresa, con default `false` (modo manual). Configurable vía `PATCH /api/v1/compras/configuracion`.**

### Modo `false` (default, manual)

- Al autorizar, el handler ejecuta cubrimiento + reservas/movimientos de almacén. **NO** llama a `IGenerarSolicitudCompraPort`.
- Si hay `CantidadDeCompra > 0` en alguna línea, la RQ pasa a `EnSurtido` con `ComprometidaEnOcId = NULL`.
- El comprador convierte manualmente:
  - **1:1**: botón "Convertir a OC" en el detalle de RQ → abre Sheet "Nueva OC" con la RQ pre-seleccionada en modo 1:1.
  - **N:1**: desde Sheet "Nueva OC" → modo Consolidación, selecciona varias RQs de la misma sucursal.
- Selector backend (`ListarRequisicionesDisponibles`) filtra `Estado=EnSurtido AND ComprometidaEnOcId=null AND sucursal=...`.
- Handlers (`CrearOrdenCompraDesdeRequisicion` 1:1, `AgregarLineaDesdeRequisicion` N:1) aceptan `EnSurtido` (no `Autorizada`) y usan `linea.CantidadDeCompra` (no `Cantidad` original) — solo el saldo de compra va a la OC; la parte de almacén ya fue cubierta con reserva+movimiento al autorizar.

### Modo `true` (automático)

- Al autorizar, el handler llama al puerto `IGenerarSolicitudCompraPort` (hoy stub `InMemoryGenerarSolicitudCompraPort` → fila en `compras.oc_borrador_stub`; mañana adapter real que crea OC real).
- La RQ pasa a `EnSurtido`. El puerto/adapter es responsable de setear `ComprometidaEnOcId`.
- En el FE: botón "Convertir a OC" oculto en RQ detalle (la OC ya existe). Sheet "Nueva OC" oculta el botón "Agregar requisiciones (consolidación)" — las RQs ya están comprometidas implícitamente, no quedan candidatas.

## Consecuencias

**Positivas**

- Resuelve la incoherencia entre RQ §A3 y OC §3.bis.1 sin tener que cancelar ninguno de los dos diseños.
- El owner cambia el modo de operación con una request HTTP, sin redeploy.
- Tests existentes que dependían del puerto stub siguen pasando — la `StubsWebApplicationFactory` seedea `AutoGenerarOcAlAutorizar=true` para preservar el comportamiento legado.
- Mantiene la puerta abierta a tener empresas distintas con modos distintos en el futuro (multi-tenant real).
- Sin storage adicional por request: el setting viaja en `LoginResponse` / `MeResponse`, el FE lo lee del store sin fetch extra.

**Negativas**

- Doble lógica en el handler de autorizar (rama if). Es testable pero agrega complejidad cognitiva.
- Dos shapes posibles del flujo Compras según el setting — el equipo de soporte tiene que conocer ambos.
- El stub `InMemoryGenerarSolicitudCompraPort` sigue activo bajo el setting `true`. El cierre del `PLATFORM-TODO(<StubsTeardown>)` se mantiene como deuda separada.

## Descartadas

**Hardcode una narrativa** (Opción 3): si en el futuro un cliente pide la opuesta, hay que rehacer ADR + cambios en handlers + tests. El setting absorbe esa volatilidad por adelantado.

**Setting global vía `appsettings.json`** (Opción 4): rompe multi-tenant. Si entran 2+ empresas con preferencias distintas, hay que migrar al modelo por empresa de todos modos. Mejor empezar bien.

**Default `true`** (Opción 2): preservaba el comportamiento histórico SAP, pero contradice el modelo mental que el owner expresó explícitamente ("nada es automático, el user debe convertir a OC"). Si una empresa pide el modo automático, lo activa con un PATCH.

## Notas de implementación

Implementado en tres PRs secuenciales (compras/oc-auto-genera-oc-setting-*):

| PR | Scope |
|---|---|
| **#159 (backend)** | Tabla `compras.settings` + entidad `ComprasSettings` + endpoints `GET/PATCH /api/v1/compras/configuracion` + payload en `LoginResponse`/`MeResponse` + `AutorizarRequisicionHandler` lee el flag + filtros `ListarRequisicionesDisponibles` cambiados a `EnSurtido + ComprometidaEnOcId IS NULL` + handlers `CrearOrdenCompraDesdeRequisicion` / `AgregarLineaDesdeRequisicion` aceptan `EnSurtido` y usan `CantidadDeCompra` + permisos nuevos `compras.configuracion.leer` / `.editar`. |
| **#160 (frontend)** | Hook `useComprasSettings()` + `useAutoGenerarOcAlAutorizar()` desde el store + función `accionConvertirAOc()` + botón "Convertir a OC" en `AccionesRequisicion` + `NuevaOrdenCompraProvider.abrir({ desdeRequisicion })` + `SheetNuevaOC` modo 1:1 + ocultar consolidación bajo setting `true`. |
| **#161 (docs)** | Este ADR + Rev. 20 de `01-diseno.md` RQ con A3 reabierta + Rev. del 01-diseno OC con cross-reference al setting. |

### Diseños actualizados

- `docs/modulos/compras-requisiciones/01-diseno.md` §3 asunción A3: "**ABIERTA con setting** — configurable por empresa vía ADR-0033". §13 Rev. 20.
- `docs/modulos/compras-ordenes-compra/01-diseno.md` §3.bis.1: nota cross-reference indicando que el filtro `EnSurtido + ComprometidaEnOcId=null` aplica a ambos modos del setting.

### Tabla `compras.settings`

```sql
CREATE TABLE compras.settings (
    id UUID PRIMARY KEY,
    empresa_id UUID NOT NULL,
    auto_generar_oc_al_autorizar BOOLEAN NOT NULL DEFAULT false,
    version INT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    created_by TEXT,
    updated_by TEXT,
    deleted_at TIMESTAMPTZ
);
CREATE UNIQUE INDEX ux_settings_empresa_id ON compras.settings(empresa_id);
```

Una fila por empresa. Migración seedea fila con default `false` para todas las empresas existentes (idempotente vía `ON CONFLICT DO NOTHING`).

### Cambio del flag en producción

```bash
# Para activar el modo automático en una empresa específica:
TOKEN=$(curl -s -X POST https://<host>/api/auth/sesion ... | jq -r .accessToken)
curl -X PATCH https://<host>/api/v1/compras/configuracion \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"autoGenerarOcAlAutorizar": true}'
```

El cambio aplica **solo a autorizaciones futuras**. RQs ya en `EnSurtido` con o sin `ComprometidaEnOcId` mantienen su estado — el setting no las retoca.

### Pendientes derivados

- Cuando el `PLATFORM-TODO(<StubsTeardown>)` se cierre y exista adapter real de `IGenerarSolicitudCompraPort`, ese adapter debe setear `ComprometidaEnOcId` en la RQ. El stub actual NO lo hace.
- Si se construye UI admin para editar el setting (Q3-b del planning, deferido), agregar página `/compras/configuracion` gateada por `compras.configuracion.editar`.
- Considerar exponer el setting también vía `AppSettingsCompras` para overrides globales de testing (las suites usan env var hoy).
