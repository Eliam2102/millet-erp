# Almacén — Ficha de usuario

## Qué hace el módulo

Controla el inventario físico de no-producción y de materiales directos
no-vidrio (interlayer, silicones, sellantes, pinturas): recibe mercancía contra
OC (con factura CFDI o con packing list), surte salidas contra requisiciones o
por vale urgente, procesa devoluciones (internas y a proveedor), levanta
inventarios físicos con captura ciega y produce los reportes de existencias.
**No** maneja vidrio crudo (sigue en A+W), no autoriza compras ni salidas (eso
viene de RQ/OC), no captura facturas para pago (CxP) y no consume material
automáticamente contra órdenes de producción.

## Quién lo usa

| Rol | Qué hace aquí |
|---|---|
| Jefe de Almacén | Aprueba ajustes mayores, cierre de mes, cierres manuales |
| Supervisor de Insumos | Captura entradas de insumos (variante A), aprueba ajustes medios |
| Almacenista de Insumos | Surte salidas, registra vales urgentes, devoluciones internas |
| Almacenista de materiales directos | Entradas con packing list (variante B), salidas diarias |
| Contador / Auditor externo | Captura ciega de conteos / solo lectura |

## Operaciones principales

| Operación | Pantalla | Pasos resumidos |
|---|---|---|
| Recepción variante A (insumos, con CFDI) | `/almacen/recepciones` → **"Nueva recepción"** → "Con factura/CFDI" | Elegir OC, cantidades, ubicación real, vincular CFDI del picker (por RFC del proveedor) o capturar el folio fiscal → **"Registrar recepción"** |
| Recepción variante B (materiales directos) | Misma pantalla → "Con packing list" | Elegir OC, subir packing list, cantidades → registrar; la factura llega después por CxP |
| Salida contra RQ | `/almacen/salidas` → **"Nueva salida"** | Elegir la RQ aprobada, líneas, sub-almacén → registrar |
| Vale urgente | Misma pantalla, variante vale | Sin RQ; queda pendiente de regularización (48 h, insignias de alerta) |
| Devolución interna (8.A) | `/almacen/devoluciones` → **"Devolución interna (8.A)"** | Referenciar la salida origen; el dañado va a Material en revisión |
| Devolución a proveedor (8.B) | `/almacen/devoluciones` → **"Nueva devolución a proveedor"** | Iniciar → Dirección autoriza (con evidencia) → registrar salida física → concilia con la NC fiscal |
| Inventario físico | `/almacen/inventarios` → **"Nuevo conteo"** | Planificar → iniciar (snapshot) → captura ciega → aprobación por monto → aplicar ajustes |
| Consultar saldos | `/almacen/saldos` y `/almacen/saldos-jerarquia` | Existencias por artículo / por estructura física |
| Reportes | `/almacen/reportes` | ALFAK-HISTORIAL-ALMACEN (cierre de mes) y EXISTENCIA-MP-CNK (diario MP) |

## Flujos internos de control

- **Reabasto automático (reorden)**: el sistema genera borradores de RQ cuando
  un artículo cae bajo su punto de reorden; se configura y activa en
  `/almacen/reorden` ("Reabasto") — guion completo en
  [P8](../pipelines/p8-almacen-reabasto-cierre.md).
- **Cierre de mes** (`/almacen/cierre-mes`): congela el periodo — exige aplicar
  o rechazar los conteos del mes y firmar o cancelar los movimientos en
  borrador; después ningún movimiento acepta fecha del mes cerrado
  ([P8](../pipelines/p8-almacen-reabasto-cierre.md)).
- **Catálogos físicos**: `/almacen/almacenes`, `/almacen/sub-almacenes`,
  `/almacen/ubicaciones` (racks N4) y `/almacen/asignaciones`
  (artículo→ubicación con política de reposición) — son prerequisito de las
  recepciones ([ficha de administración](almacen-admin.md)).
- **Consulta de saldos**: `/almacen/saldos` (plano) y
  `/almacen/saldos-jerarquia` (por estructura física).
- **Ajustes manuales** (`almacen.ajustes.manual`): restringidos y auditables;
  el camino normal para corregir existencias es el conteo con aprobación.

## Ejemplos con folios reales

- **`M-ENT2026-000008`** — recepción variante A con CFDI vinculado, dentro del
  ciclo completo de compra: [P1](../pipelines/p1-flujo-feliz-insumos.md).
- **`M-ENT2026-000009`** — recepción variante B con packing list, conciliada
  después por la factura `P2-001`: [P2](../pipelines/p2-materiales-directos.md).
- **`M-DEV2026-000001`** — devolución a proveedor que terminó
  `ConciliadaConNcFiscal` con la nota de cargo `NCG-2026-000001`:
  [P3](../pipelines/p3-devolucion-proveedor.md).
- **`M-SAL2026-000004/5`, `M-DEV2026-000002`, `M-AJN2026-000001`** — salida,
  vale, devolución interna y ajuste de conteo:
  [P6](../pipelines/p6-flujos-internos-almacen.md).

## Errores comunes (en lenguaje de negocio)

- **"El artículo no está asignado a la ubicación elegida. Asígnalo primero."** —
  falta la asignación artículo→ubicación; pedirla al administrador del almacén
  (es la causa #1 de recepciones bloqueadas).
- **"Las entradas exigen una ubicación real; la ÚNICA solo se drena por
  salidas."** — elegir un rack/ubicación concreta al recibir.
- **"La recepción variante A requiere el CFDI vinculado o su folio fiscal
  (UUID)."** — sin factura no hay recepción de insumos; capturar al menos el
  UUID del CFDI.
- **"La RQ '{folio}' está en estado '{estado}'; no acepta salida."** — solo se
  surten RQs autorizadas.
- **"El sub-almacén está en conteo anual; las salidas están bloqueadas..."** —
  esperar a que el conteo se aplique o rechace.
- **"El material dañado debe enviarse a un sub-almacén MaterialEnRevision."** —
  las devoluciones internas de material dañado no regresan a stock disponible.
- Los movimientos registrados **no se editan**: cualquier corrección es un
  contramovimiento nuevo — no es un defecto, es la regla de inmutabilidad.
