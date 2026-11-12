# Contracts · N-15

- Evento almacen.oc_recepcion.registrada.v1: formato sin cambios. Validar datos existentes;
  cantidad aplicada se obtiene del puerto canónico, no del orden de llegada del snapshot.
- IRecepcionesImportacionReadPort.ObtenerCantidadesRecibidasAsync(ocId,ct): neto por línea;
  cálculo dueño de Almacén. Actualizar implementación y dobles/construcciones manuales.
- Rutas CE revisar-documentos/cerrar-fisico/cerrar-documental/cerrar-financiero/cerrar:
  mismo permiso, sucursal, If-Match, Idempotency-Key y respuesta. Recuperar recepción antes
  de validar la acción;422 no debe anunciar éxito ni persistencia de la recuperación.
- Hitos/incidencias: mismo contrato; responsable con bypass efectivo de gestión CE es elegible
  sin asignación adicional. Lectura global sola, permiso ajeno o denegado no lo hacen elegible.
- No nuevos endpoints, permisos, suscripciones ni campos de eventos.
