# Órdenes de Compra — Ficha de usuario

## Qué hace el módulo

Formaliza la compra con el proveedor: convierte requisiciones autorizadas en
órdenes de compra (1:1 o consolidando varias), las pasa por doble autorización
(Jefe de Compras y Dirección), genera el PDF al proveedor y sigue la vida de la
orden con **tres sub-estados independientes** — Recepción, Facturación y Pago —
que avanzan con lo que reportan Almacén, CxP y Tesorería. **No** ejecuta pagos,
no recibe mercancía, no captura facturas y no administra los catálogos de
proveedores/artículos (solo los consume).

## Quién lo usa

| Rol | Qué hace aquí |
|---|---|
| Comprador | Crea OCs (desde RQ, consolidadas o sin RQ), adjunta documentos, corrige OCs rechazadas por tolerancia |
| Jefe de Compras | Autoriza Nivel 1 |
| Dirección | Autoriza Nivel 2; segunda firma en cancelaciones con recepciones |
| Almacén / CxP / Tesorería | No operan aquí; sus eventos mueven los sub-estados |

## Operaciones principales

| Operación | Pantalla | Pasos resumidos |
|---|---|---|
| Crear OC | `/compras/ordenes` → **"Nueva OC"** (o **"Convertir a OC"** desde una RQ) | Tres modos del Sheet: vacía (sin RQ, exige motivo + correo de autorización), 1:1 desde RQ, consolidación N:1 con el selector de RQs (una sola sucursal) |
| Adjuntar documentos | `/compras/ordenes/$id` → gestor de adjuntos | Cotización (obligatoria para transmitir), ficha técnica (importación), correo de autorización (sin RQ o excepción de cotización) |
| Transmitir y autorizar | Detalle → **"Transmitir a autorización"**, **"Aprobar Nivel 1"**, **"Aprobar Nivel 2"** | Doble firma; **"Rechazar"** regresa a Borrador con motivo |
| Seguir sub-estados | `/compras/ordenes` (columnas Recepción·Facturación·Pago) y detalle (barra de sub-estados) | Solo lectura; avanzan por eventos de la triada |
| Cancelar / duplicar | Detalle → **"Cancelar OC"** / **"Cancelar (doble firma)"** / **"Duplicar OC"** | Post-autorización no se edita: se cancela y recrea (duplicar prellena) |
| Partidas abiertas | `/compras/ordenes/partidas-abiertas` | Reporte de líneas sin recibir/facturar, con KPIs |
| Trazabilidad | `/compras/trazabilidad/oc/$id` | Árbol RQ → OC → recepciones → facturas |

## Flujos internos de control

- **Información logística post-autorización**: transportista, guía y contenedor
  se editan sobre la OC autorizada con el permiso específico de logística, sin
  reabrir la autorización (única edición permitida post-firma).
- **Partidas abiertas** (`/compras/ordenes/partidas-abiertas`): control diario
  del comprador — líneas sin recibir o sin facturar, con KPIs y días de atraso.
- **Trazabilidad** (`/compras/trazabilidad/oc/$id`): árbol completo
  RQ → OC → recepciones → facturas para auditoría de cualquier orden.
- **Encargado de compras reasignable**: el comprador titular (creador) es
  inmutable; el encargado puede reasignarse para cubrir ausencias.
- **Configuración del módulo**: settings de Compras (p. ej. generación
  automática de OC al autorizar RQ, default apagada) con permisos
  `compras.configuracion.*`.

## Ejemplos con folios reales

- **`OC-MID2026-000020`** — 1:1 desde la RQ `MID2026-000040`, ciclo completo con
  recepción variante A, factura `P1-001` y pago:
  [P1](../pipelines/p1-flujo-feliz-insumos.md).
- **`OC-MID2026-000021`** — OC directa **sin RQ** para materiales directos, con
  packing list y factura posterior `P2-001`:
  [P2](../pipelines/p2-materiales-directos.md).
- Una factura del proveedor con precio distinto al de la OC se rechaza sola y
  regresa el problema al comprador: [P4](../pipelines/p4-rechazo-tolerancia.md).

## Errores comunes (en lenguaje de negocio)

- **"Antes de enviar a autorización, la OC requiere un adjunto tipo
  'cotizacion'..."** — subir la cotización, o activar la excepción y adjuntar el
  correo de autorización.
- **"OC sin requisición previa requiere adjunto tipo 'correo_autorizacion'."** —
  toda OC directa necesita el respaldo por escrito de quien la aprobó.
- **"La OC todavía tiene campos TBD en la cabecera..."** — completar proveedor,
  condiciones, uso y almacén antes de transmitir.
- **"El proveedor '{clave}' está Inactivo..."** — el proveedor debe estar activo
  en cada transición; pedir su reactivación a Datos Maestros.
- **"Ya existe una autorización exitosa de nivel {N} para esta OC."** — la firma
  ya quedó; refrescar la bandeja.
- La OC autorizada no se puede editar: **cancelar + duplicar** es el camino
  correcto, no es un defecto.
