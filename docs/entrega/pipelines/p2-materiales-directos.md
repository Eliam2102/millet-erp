# P2 — Materiales directos (recepción variante B, packing list)

**Cadena:** OC directa sin RQ → recepción con packing list (factura pendiente)
→ factura posterior concilia la recepción.

**Evidencia en dev (2026-07-15):** OC `OC-MID2026-000021` · recepción
`M-ENT2026-000009` · factura `P2-001` ($1,000.50, diferencia de $0.50 dentro de
tolerancia).

## Objetivo de negocio

Comprar materiales directos de producción no-vidrio (interlayer, silicones,
sellantes, pinturas). A diferencia de los insumos, el proveedor entrega el
material con **packing list** y la factura llega días después por CxP. La
recepción queda marcada `factura_pendiente=true` hasta que la factura la
concilia. No hay RQ previa: el comprador crea la OC directa, lo cual exige un
correo de autorización adjunto.

## Actores y permisos

| Paso | Actor | Permisos requeridos |
|---|---|---|
| Crear OC sin RQ | Comprador | `compras.ordenes.crear-sin-rq`, `.adjuntar` |
| Autorizar OC | Jefe de Compras (N1), Dirección (N2) | `compras.ordenes.autorizar-nivel1` / `.autorizar-nivel2` |
| Recepción con packing list | Almacenista de materiales directos | `almacen.entradas.capturar`, `.registrar` |
| Capturar factura posterior | Auxiliar de CxP | `cuentas_por_pagar.facturas.capturar` |

## Precondiciones

1. Proveedor y artículo activos; **tolerancia de monto del proveedor**
   configurada (o default global $0.99 MXN).
2. **Asignación artículo→ubicación** para el sub-almacén de materiales directos
   (mismo requisito que P1: sin ella, `ENTRADA_SIN_ASIGNACION`).
3. Documento **packing list** digitalizado para adjuntar.
4. **Correo de autorización** (email de quien aprueba la compra sin RQ)
   digitalizado.

## Pasos

1. **Crear la OC directa.** En `/compras/ordenes` ("Bandeja de OCs"), botón
   **"Nueva OC"** → Sheet en modo *vacío* (sin RQ). Capturar proveedor, almacén
   destino, condiciones y líneas. Al no venir de RQ, capturar el
   **motivo sin requisición** y adjuntar el **`correo_autorizacion`** — sin
   ellos, transmitir rechaza con `OC_SIN_RQ_MOTIVO_REQUERIDO` /
   `OC_SIN_RQ_CORREO_REQUERIDO`. Adjuntar también la cotización (regla general).
   📸 Captura pendiente: Sheet "Nueva OC" modo sin RQ.
2. **Transmitir y autorizar.** **"Transmitir a autorización"** → "Aprobar
   Nivel 1" (Jefe de Compras) → "Aprobar Nivel 2" (Dirección). OC
   `OC-MID2026-000021` queda `Autorizada` → `OrdenCompraAutorizadaEvent`.
3. **Recibir con packing list.** En `/almacen/recepciones`, **"Nueva
   recepción"** → tipo **"Con packing list"** (variante B — "Materiales directos
   no-vidrio — factura llega después por CxP"). Seleccionar la OC, subir el
   packing list, capturar cantidades y ubicación real. **"Registrar recepción"**
   → `M-ENT2026-000009` firme, con bandera de **factura pendiente**.
   📸 Captura pendiente: Sheet "Nueva recepción" variante B.

> **Evento emitido:** `OcRecepcionRegistradaEvent` con `factura_pendiente=true`
> (Almacén → Compras y CxP). El inventario se valúa al **precio de la OC**.

4. **Capturar la factura cuando llegue.** Días después, el CFDI del proveedor
   entra al repositorio (`/cxp/cfdis`). El Auxiliar de CxP captura la factura en
   `/cxp/facturas` → **"Capturar factura"** contra `OC-MID2026-000021`. La
   conciliación 3-way compara: factura $1,000.50 vs OC $1,000.00 → diferencia
   **$0.50 ≤ tolerancia $0.99** → pasa, y la diferencia se registra como
   `Redondeo`. La factura `P2-001` queda `Capturada` y después `Autorizada`
   (paso 10 de [P1](p1-flujo-feliz-insumos.md)).

> **Eventos emitidos:** `FacturaProveedorRegistradaEvent` (→ Compras sub-estado
> Facturación; → Almacén, que **concilia la recepción pendiente** de variante B).
> Si además el **precio unitario** difiere del de la OC (dentro de tolerancia),
> CxP emite `DiferenciaPrecioFacturaDetectadaEvent` (GAP-3 resuelto en #617):
> Almacén genera un movimiento `AjustePrecioFactura` que re-valoriza solo la
> **cantidad remanente en stock** y avisa a Contabilidad; la entrada original
> se mantiene al **precio de la OC** (regla de ambas variantes) y Compras lo
> recibe como informativo.

El árbol de decisión completo de la tolerancia (redondeo vs rechazo) está
diagramado en [P4 — "La decisión de un vistazo"](p4-rechazo-tolerancia.md).

## Cómo verificar el resultado en cada módulo

| Módulo | Dónde | Qué esperar |
|---|---|---|
| Compras | `/compras/ordenes` | `OC-MID2026-000021` `Autorizada`; sub-estados Recepción=Completa y Facturación=Completa |
| Almacén | `/almacen/recepciones/$id` | `M-ENT2026-000009` `Registrada`; la conciliación con la factura queda asentada. ⚠️ Hallazgo menor abierto: la bandera `factura_pendiente` de la proyección local no se apaga al conciliar — no afecta saldos |
| CxP | `/cxp/facturas/$id` | `P2-001` capturada con diferencia $0.50 asentada como redondeo |

## Variantes y errores esperados

| Situación | Error real | Mensaje |
|---|---|---|
| Transmitir OC sin RQ sin motivo | `OC_SIN_RQ_MOTIVO_REQUERIDO` | "OC sin requisición previa requiere capturar MotivoSinRequisicion." |
| Transmitir OC sin RQ sin correo | `OC_SIN_RQ_CORREO_REQUERIDO` | "OC sin requisición previa requiere adjunto tipo 'correo_autorizacion'." |
| OC de importación sin ficha técnica | `OC_FICHA_TECNICA_REQUERIDA` | "Una OC de importación requiere adjunto tipo 'ficha_tecnica' antes de enviar a autorización." |
| Cabecera incompleta al transmitir | `OC_TRANSMITIR_BORRADOR_MINIMO` | "La OC todavía tiene campos TBD en la cabecera (proveedor / condiciones / uso / almacén). Completar antes de transmitir." |
| Entrada sin asignación artículo→ubicación | `ENTRADA_SIN_ASIGNACION` | "El artículo no está asignado a la ubicación elegida. Asígnalo primero." |
| Factura fuera de tolerancia | — | Cancelación automática con motivo `RechazadaPorTolerancia` — ver [P4](p4-rechazo-tolerancia.md) |
