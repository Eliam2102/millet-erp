# Data model: reutilizar entidades y proteger históricos

## OC de servicio
Reutilizar OrdenCompra.Id, EmpresaId, SucursalId, ProveedorId, Moneda y Autorizaciones. Indicador local `EsServicioComercioExterior` explícito,false por omisión. N1/N2 conservan actor, nivel, estado e historia. No marcar retrospectivamente todas las compras de servicio comoCE. Copia/duplicación debe explicar si retiene el indicador sin retener firmas.

## Factura y gasto
Una factura vigente completa porOCservicio. FacturaProveedor mantiene importes, moneda, deuda yUUID/origen; CE enlaza `FacturaProveedorId` enGastoExpediente. Cancelar no borra; recaptura conserva cadena. N2 debe conservar relación verificable con la factura revisada; ensayar sustitución antes de decidir si bastan guardas existentes o se necesita identidad/revisión en autorización. No diseñar una tabla nueva antes de reproducir el riesgo.

## Pagos y cierre
AplicacionPagoProveedor apunta a FacturaProveedorId yMovimientoId; sólo aplicaciones no revertidas y de la empresa. CxP mantiene SaldoPendiente/ImportePagado; adapter consulta pagosTesorería vigentes para detectar reversa o proyección pendiente. Motivo incluye referencia/moneda sin filtrar otras empresas. CierreIMPO reconsulta bajo controles de versión antes de confirmar financiero oGerencia; detalle explicativo no es autoridad suficiente para mutar.

## Transiciones observables
OCservicio nueva→N1pendiente→N1firmada/pasivopendiente→pasivocapturado/N2pendiente→N2firmada→CxPliberada→pago/aplicación→saldo0→cierreposible. Rechazo/cancelación/sustitución invalidan las autorizaciones que dejen de corresponder al documento vigente; ninguna transición crea un segundo pasivo. Crédito normal no creaanticipo. Anticipo/pedimento conservan su identidad separada.

## Migraciones
Reusar las3aditivaslocales sólo tras integrar modelos actuales. Snapshots preservan RQ/M096/avisos y migraciones posteriores. Ensayar base vacía y actualización desde historialQA; históricos consultables con false. Permisos nuevos sin asignaciones automáticas. Reversión del pase con respaldo/versión anterior, no borrar columnas con documentos nuevos por reflejo.
