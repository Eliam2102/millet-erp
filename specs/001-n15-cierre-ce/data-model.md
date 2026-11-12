# Data Model · N-15

Sin tablas, columnas ni migraciones nuevas.

- IRecepcionesImportacionReadPort: nueva consulta neta por OcId, diccionario LineaOcId→decimal,
  en unidad de OC con redondeo4 existente. Línea desconocida se rechaza; ausencia no se asume cero.
- PartidaImportacion: conservar CantidadRecibida, FechaRecepcion, CompromisoRecepcion y
  EventosRecepcion. Un ID sólo se añade cuando procede de un evento real. Recuperación humana
  puede actualizar la conciliación sin añadir IDs fabricados.
- Incidencia automática: misma identidad/historia; neto diferente actualiza diferencia y estado;
  neto igual no añade historia repetida. Diferencia real sigue abierta; cero permite cierre automático.
- Usuario/roles/alcance: no cambian datos. Se consulta permiso efectivo en empresa de sucursal;
  denegación explícita prevalece; actor y responsable se evalúan por separado.
- Expediente: ordenes_compra_ids existente; cancelados fuera del reparto; cerrados reservan su
  consumo sin mutación; no se crean recepciones ni se modifica cantidad pedida/embarcada.
