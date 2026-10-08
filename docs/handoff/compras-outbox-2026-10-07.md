# Compras: persistencia transaccional de eventos · 07-oct-2026

Estado: arreglo local sin commit ni push. Build verificado; ejecución de pruebas pendiente por restricciones del sandbox. No acredita entrega por Service Bus, integración real con CxP ni aceptación del incidente original.

## Causa confirmada en código

`OutboxIntegrationEventPublisher` agrega eventos al `InMemoryIntegrationEventBuffer` scoped. `OutboxSaveChangesInterceptor` drena solo el schema correspondiente durante `SavingChanges` y agrega filas al mismo DbContext. Publicar después del último guardado deja el evento en memoria.

`AutorizarOrdenCompraHandler` guardaba antes de publicar `OrdenCompraAutorizadaEvent`. `OcAutorizadaMapper` y `OrdenCompraAutorizadaPdfListener` recibían esa misma notificación. MediatR se registra mediante escaneo de assemblies, sin prioridad explícita de estos handlers. El publisher predeterminado es secuencial: el defecto es depender del orden de registro, no una ejecución paralela demostrada. Si PDF guardaba primero, el mapper posterior dejaba su evento sin persistir; en el orden inverso el PDF lo guardaba en una transacción posterior, tampoco atómica con la autorización.

No se reprodujo la OC del incidente ni se modificaron datos locales reales.

## Solución

Publicar los eventos que tienen mapper antes de `SaveChangesAsync`, siguiendo ADR-0009 y el patrón ya aplicado en Requisiciones. No agregar un segundo guardado como reparación: sin transacción explícita mantendría una ventana entre estado y outbox.

Para autorización, emitir después del guardado una nueva notificación local, `OrdenCompraAutorizadaPersistida`, exclusiva del PDF. El evento de dominio original se publica una sola vez, antes de guardar. El PDF mantiene su consulta y guardado posteriores; un fallo del PDF no revierte una autorización ya persistida, como ocurría antes, pero ahora esa autorización incluye su outbox.

Se corrigen también cierres y reaperturas derivados de recepción, factura, nota de crédito, pago y devolución. En el consumidor de recepción de Almacén, el cierre se encola antes del guardado de OC + marca de idempotencia; la actualización de RQ conserva su tratamiento posterior best-effort.

## Revisión de la lista solicitada

| Operación | Hallazgo y acción |
|---|---|
| Autorizar OC | Mapper de autorización; publicar antes del guardado y separar PDF |
| Rechazar OC | Mapper de rechazo; publicar antes del guardado |
| Enviar OC a autorización | Mapper de envío; publicar antes del guardado |
| Cancelar OC | Mapper de cancelación; publicar antes del guardado |
| Cancelar OC con recepciones | Mismo mapper; publicar antes del guardado |
| Cerrar OC manualmente | Mapper de cierre; publicar antes del guardado |
| Eliminar línea OC | `LineaRqLiberadaEvent` sin mapper de integración; sin cambios |
| Agregar línea desde RQ | `RqComprometidaEnOcEvent` sin mapper de integración; sin cambios |
| Crear OC desde RQ | `RqComprometidaEnOcEvent` sin mapper de integración; sin cambios |
| Duplicar OC | `OrdenCompraDuplicadaEvent` sin mapper de integración; sin cambios |
| Autorizar requisición | Ya publica antes del guardado final dentro de transacción explícita; sin cambio productivo, nueva prueba de protección |

## Archivos productivos cambiados

Rutas relativas a `backend/src/Compras/Application/`:

- `Oc/Autorizar/AutorizarOrdenCompraHandler.cs`
- `Oc/Cancelar/CancelarOrdenCompraHandler.cs`
- `Oc/CancelarConRecepciones/CancelarConRecepcionesHandler.cs`
- `Oc/CerrarManual/CerrarManualOcHandler.cs`
- `Oc/EnviarAAutorizacion/EnviarAAutorizacionOcHandler.cs`
- `Oc/Rechazar/RechazarOrdenCompraHandler.cs`
- `Oc/Eventos/OrdenCompraAutorizadaPersistida.cs` (nuevo)
- `Oc/Eventos/OrdenCompraAutorizadaPdfListener.cs`
- `Oc/Eventos/FacturaProveedorRegistradaListener.cs`
- `Oc/Eventos/NotaCreditoProveedorRegistradaListener.cs`
- `Oc/Eventos/OcDevolucionRegistradaListener.cs`
- `Oc/Eventos/PagoFacturaProveedorListener.cs`
- `Oc/Eventos/RecepcionMaterialEnOcListener.cs`
- `Almacen/OcRecepcionEnAlmacenCommand.cs`

## Pruebas nuevas

En `backend/tests/Compras.IntegrationTests/Outbox/OperacionesComprasOutboxTests.cs` (nuevo):

1. Autorizar OC sin listener de PDF: exactamente un evento persistido, verificado desde otro scope. Quitar el listener evita que un guardado accidental o el orden de handlers oculte la regresión.
2. Cancelar OC: exactamente un evento y estado Cancelada desde otro scope.
3. Fallo de PDF: autorización y evento ya persistidos.
4. PDF exitoso: un puerto de prueba consulta estado + outbox desde otro scope antes de generar; después se verifica una fila PDF y ningún evento duplicado.
5. Rollback explícito: evento visible dentro de la transacción y, tras rollback, ni evento ni cambio de estado persistidos.

En `backend/tests/Compras.IntegrationTests/Bifurcacion/BifurcacionEndpointsTests.cs`:

6. Autorizar requisición por HTTP: exactamente un evento correlacionado por RequisicionId y estado EnSurtido.

Las regresiones de OC están diseñadas para fallar con el orden de guardado anterior. La de requisición protege comportamiento que ya era correcto, por lo que no se espera que falle en la base. La ejecución rojo/verde queda **Por confirmar** por falta de acceso a Docker.

## Otros módulos

Revisión estática de productores y guardados en Application, y búsqueda de publicaciones posteriores al último guardado en los módulos solicitados. No se identificó otro caso equivalente confirmado; no se modificaron esos módulos.

- **Almacén:** recepción con packing list y salida con requisición publican antes de guardar. En `DiferenciaPrecioFacturaDetectadaHandler`, el guardado anterior pertenece a una rama que retorna sin evento; la rama que publica guarda después.
- **CxP:** captura, autorización/cancelación de factura y mappers publican antes del guardado de la operación. Los archivos con múltiples handlers pueden dar falsos positivos al buscar solo por posición textual.
- **Tesorería:** aplicación/reversión de pago y solicitud de cancelación publican antes de guardar.
- **Facturación:** emisión guarda después de encolar; el guardado temprano de `EmitirFacturaVentaHandler` retorna por una rama distinta. En `ReintentarTimbradoHandler`, los helpers publican y el método Handle guarda al regresar de ellos.
- **CxC:** liberación y propuestas publican antes de guardar; el helper de alertas encola y el caller guarda cuando creó alertas.

Esta inspección no sustituye pruebas de integración de esos módulos.

## Validación y siguiente acción

- Dependencias restauradas desde la caché NuGet local, sin cambiar referencias.
- `dotnet build Millet.sln --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false`: **correcto, 0 advertencias, 0 errores**, incluida la compilación de las seis pruebas nuevas.
- `dotnet test backend/tests/Compras.UnitTests/Millet.Compras.UnitTests.csproj --no-build --no-restore --disable-build-servers -m:1`: **anulado antes de ejecutar pruebas**, VSTest no puede abrir su socket local (`SocketException (13): Permission denied`). No hay conteo aprobado.
- `bash tools/validate-integration-isolated.sh`: **bloqueado antes de ejecutar las suites**, permiso denegado sobre `/Users/eliamcv/.orbstack/run/docker.sock`. No se forzó el acceso.
- `git diff --check`: correcto.
- Sin cambios en `infra/`, `frontend/` ni `.env*`; sin commits ni push.

Claude debe ejecutar las unitarias y el gate completo fuera del sandbox. Si el conteo base proporcionado sigue vigente, el esperado es Integraciones.Aw 7, Compras 137 (131 + 6), Api 813; son conteos esperados, no resultados obtenidos. Después conviene repetir una autorización con Service Bus y verificar outbox + consumo CxP.

Esta nota sirve también como borrador local de actualización de la base de conocimiento. No se actualizó la bóveda Obsidian, ubicada fuera de las rutas de escritura permitidas.

Mensaje de commit propuesto:

`fix(compras): persistir eventos de integración junto con los cambios de estado`
