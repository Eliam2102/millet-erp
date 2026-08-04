# Runbook — Submódulo Órdenes de Compra

> Procedimientos operativos para casos no triviales del submódulo OC.
> Audiencia: ops + soporte L2. **No incluye** flujo normal del usuario
> final (eso vive en la documentación del producto).

---

## Cancelar OC con factura asociada

Una OC ya facturada (factura del proveedor registrada en CxP) **no
puede cancelarse directamente**. El proceso correcto es:

1. **Validar con CxP** que la factura sea realmente cancelable
   (puede haber notas de crédito asociadas, pagos parciales, etc.).
2. **CxP cancela la factura** vía su proceso normal — eso emite
   `cxp.factura-proveedor.cancelada.v1` que un futuro listener de OC
   ajustaría sub-estado de facturación.
3. Una vez la factura está cancelada, la OC vuelve a estado
   compatible con `CancelarConRecepcionesParciales`:
   `POST /api/v1/compras/ordenes/{id}/cancelar-con-recepciones`
   (requiere 3 permisos: `compras.ordenes.cancelar-doble` +
   `compras.ordenes.autorizar-nivel1` + `compras.ordenes.autorizar-nivel2`).
4. La cancelación libera el saldo no recibido al pool de RQ
   automáticamente (F4-PR3 + F5-PR4).

**Auditoría**: cada operación queda en `core.audit_log` con
`AggregateRootId = ocId`. El historial completo se consulta vía
`GET /ordenes/{id}/historico` (F7-PR3).

---

## Duplicar una OC cancelada o rechazada (C4)

Caso de uso: corregir errores en una OC sin perder la cabecera ni
los items.

1. Verificar que la OC origen esté en `Cancelada` o `Rechazada`.
2. `POST /api/v1/compras/ordenes/{id}/duplicar` con body:
   ```json
   { "sucursalCodigo": "MID", "folioAnio": 2026, "fechaDocumento": "2026-..." }
   ```
3. La OC nueva nace en `Borrador` con `OcOrigenId` apuntando a la
   origen. Líneas se copian como **manuales** (sin FK a RQ — las RQs
   originales ya fueron liberadas al cancelar/rechazar la origen).
4. El comprador edita los campos que necesite y la transmite.
5. Para navegar de la OC nueva a su origen:
   `GET /ordenes/{idNueva}/origen` devuelve `{ ocOrigenId, folioOrigen }`
   (o 204 si no fue duplicada).

---

## Investigar un conflicto 409 (concurrencia optimista)

El header `If-Match` con `ETag` (igual a `Version` del agregado) se
usa en mutaciones (PATCH/POST a recursos existentes). Si dos usuarios
modifican la misma OC simultáneamente, el segundo recibe `409 Conflict`.

**Procedimiento de diagnóstico**:

1. Pedir al usuario el `OrdenCompraId` del 409.
2. Consultar `GET /ordenes/{id}` y observar el `Version` actual.
3. Cruzar con `core.audit_log` filtrando por `entity_id` para ver
   los cambios recientes y por quién:
   ```kql
   AzureDiagnostics
   | where Resource has "psql"
   ```
   o vía endpoint `GET /ordenes/{id}/historico`.
4. Si el usuario tiene una versión vieja en su UI, se le pide que
   recargue (Ctrl+F5) y reintente. El frontend debería detectar el
   409 y mostrar "el documento fue modificado por otro usuario,
   por favor recarga".

---

## PDFs corruptos o no descargables

Si un usuario reporta que el PDF de una OC no se descarga o se ve
roto:

1. Confirmar que la OC está `Autorizada` o `Cerrada` (el PDF se
   genera al autorizar N2).
2. Consultar `compras.orden_compra_pdf` para verificar:
   ```sql
   SELECT id, generado_por, generado_en, blob_url, tamano_bytes
   FROM compras.orden_compra_pdf
   WHERE orden_compra_id = '<guid>';
   ```
3. Si `tamano_bytes` es muy pequeño (<1KB), el PDF puede estar
   corrupto. Soluciones:
   - **Re-generar**: rechazar y re-autorizar la OC fuerza
     `PUT semantics` — el listener `OrdenCompraAutorizadaPdfListener`
     reemplaza el blob.
   - Para evitar afectar al usuario, esta operación se programa fuera
     de horario o el comprador acepta el re-autorizar.
4. Si `generado_por = "LocalPdfOrdenCompraStub"`, está usando el
   stub viejo (pre-F6-PR3). Re-autorizar para que se regenere con
   QuestPDF.

---

## Conexión Azure Blob falla

Si los uploads de adjuntos o el PDF fallan con
`Azure.Storage.RequestFailedException`:

1. Verificar la connection string en Key Vault:
   `Compras:Oc:BlobStorage:ConnectionString`.
2. Verificar que el container `compras-oc-blobs` exista (se crea
   automáticamente en arranque; si Azure rotó claves, recrearlo).
3. Si la connection string no aparece, el wireup cae al stub local
   filesystem — esto es **detectable** en logs (mensaje
   `LocalFilesystemBlobStub`). En Production esto es síntoma de
   misconfig — alertar y revisar el deploy.

---

## Bibliografía interna

- `docs/modulos/compras-ordenes-compra/01-diseno.md` — diseño completo.
- `docs/integraciones/compras-eventos.md` — contratos de integration events.
- `docs/operacion/dashboards-compras-oc.md` — observabilidad y KPIs.
- `docs/operacion/compras-oc-benchmark-metodologia.md` — perf.
