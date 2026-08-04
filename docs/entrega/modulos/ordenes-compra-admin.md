# Órdenes de Compra — Ficha de administración

## Permisos canónicos

| Permiso | Rol sugerido |
|---|---|
| `compras.ordenes.leer` | Todos los usuarios del módulo |
| `compras.ordenes.crear` | Comprador |
| `compras.ordenes.crear-sin-rq` | Comprador senior (FOC11 — compra directa) |
| `compras.ordenes.adjuntar` | Comprador |
| `compras.ordenes.logistica` | Comprador (transportista/guía post-autorización) |
| `compras.ordenes.autorizar-nivel1` | Jefe de Compras |
| `compras.ordenes.autorizar-nivel2` | Dirección |
| `compras.ordenes.cancelar` | Comprador senior |
| `compras.ordenes.cancelar-doble` | Dirección (OC con recepciones parciales) |
| `compras.ordenes.reportes-partidas-abiertas` | Comprador / Jefe de Compras |
| `compras.configuracion.leer` / `.editar` | Administrador del módulo |

## Configuración previa obligatoria

1. **Catálogo `tipos_documento_oc` completo** (seed): `cotizacion`,
   `ficha_tecnica`, `correo_autorizacion`, etc. Si el seed está incompleto, el
   backend aborta con `TIPO_DOCUMENTO_SEED_INCOMPLETO`.
2. **Regla de adjuntos** (la verificación la confirmó bloqueante):
   - transmitir exige adjunto **`cotizacion`** o la bandera de excepción **más**
     un **`correo_autorizacion`**;
   - OC **sin RQ** exige motivo capturado **y** adjunto `correo_autorizacion`;
   - OC de **importación** exige `ficha_tecnica`.
3. **Tolerancia de cantidad por línea** — se fija al crear la OC; Almacén la usa
   para el cierre automático de la recepción. (Es distinta de la tolerancia de
   monto por proveedor de CxP y de la de cantidad por material de Almacén.)
4. Catálogos consumidos activos: proveedores, artículos, condiciones de pago,
   incoterms, transportistas (seeds; sin CRUD en este módulo).
5. Secuencia de folios por sucursal/año: `OC-{prefijo}{año}-{secuencial:6}`.

## Eventos que publica / consume

| Dirección | Evento | Con quién | Efecto operativo |
|---|---|---|---|
| Publica | `OrdenCompraAutorizadaEvent` | Almacén, CxP | Habilita recepción y conciliación de factura |
| Publica | `OrdenCompraCanceladaEvent` | Almacén, CxP, Requisiciones | Libera RQs comprometidas |
| Publica | `OrdenCompraCerradaEvent` | Almacén (informativo), Requisiciones | Cuando Recepción+Facturación+Pago llegan a Completa — **Compras calcula el cierre, nadie más** |
| Consume | `OcRecepcionRegistradaEvent` (Almacén) | topic `almacen-events` | Avanza sub-estado Recepción |
| Consume | `FacturaProveedorRegistradaEvent` / `...CanceladaEvent` / `...RechazadaPorToleranciaEvent` (CxP) | topic `cuentas-por-pagar-events`, subscription `compras-subscription` | Avanza/corrige sub-estado Facturación (acumulados calculados por CxP) |
| Consume | `cuentas_por_pagar.factura.pago-aplicado.v1` (CxP, proxy de Tesorería) | topic `cuentas-por-pagar-events`, subscription `compras-subscription` | Avanza sub-estado Pago con el acumulado por OC calculado por CxP; al completar los 3 sub-estados la OC cierra sola (#613/#614) |
| Consume | `OcDevolucionRegistradaEvent` (Almacén) | topic `almacen-events` | Decrementa `CantidadRecibida` de la línea devuelta; reabre la OC si estaba `Cerrada` (GAP-5, #614) |

## Monitoreo y troubleshooting

- **Sub-estado de OC que no avanza:** primero revisar dead-letters de la
  subscription correspondiente en Service Bus (`compras-subscription` para
  facturación); luego el log del `CxpEventListenerWorker` (en Compras) en
  Application Insights. Incidente conocido ya corregido (#610): el listener no
  toleraba mensajes en PascalCase y el sub-estado Facturación nunca avanzaba.
- **Idempotencia:** los consumidores correlacionan por `recepcion_id` /
  `factura_id`; reprocesar un dead-letter es seguro.
- **Cierre que no ocurre:** verificar los tres sub-estados en la bandeja. Si el
  de Pago no avanza, revisar dead-letters de `compras-subscription` y que las
  **reglas de la subscription** incluyan `factura.pago-aplicado.v1` y
  `oc_devolucion.registrada.v1` — en dev se actualizan a mano (runbook de
  #614); QA/prod las reciben con el deploy de infra.

## Límites conocidos vigentes

- **NC de proveedor sin granularidad por línea de OC**: el listener
  `NotaCreditoProveedorRegistradaListener` está huérfano (PLATFORM-TODO
  `<NcGranularidadLineaOc>`); `cuentas_por_pagar.nota-credito.registrada.v1`
  solo se loggea.
