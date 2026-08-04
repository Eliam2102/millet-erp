# ADR-0010: Manejo de errores end-to-end con Problem Details (RFC 7807)

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: api, errores, fundación

## Contexto y problema

Los errores en una aplicación distribuida toman muchas formas: validación
fallida de un comando, excepciones inesperadas en infraestructura, conflictos
de concurrencia, timeouts a sistemas externos, errores de negocio explícitos
("no puedes cancelar un CFDI ya pagado"). Cada uno necesita ser representado,
correlacionado con logs, y mostrado al usuario de manera útil.

Sin una convención uniforme, cada controlador termina manejando errores de
manera distinta, el frontend tiene que parsear formatos heterogéneos, y los
logs no se correlacionan con lo que ve el usuario.

## Drivers de la decisión

- Formato uniforme de error en todas las respuestas HTTP
- Diferenciación entre errores de cliente (400, 422) y de servidor (500)
- Correlación con logs (mismo `traceId` en log y en respuesta)
- Información útil para el usuario sin filtrar detalles internos sensibles
- Estándar conocido (no inventar formato propio)

## Opciones consideradas

1. Problem Details (RFC 7807) con extensiones del proyecto
2. Formato propio
3. JSend
4. Devolver excepciones serializadas tal cual

## Decisión

Se adopta **Problem Details (RFC 7807)** como formato estándar de errores HTTP,
con extensiones específicas del proyecto.

**Forma de la respuesta**:

```json
{
  "type": "https://millet-erp/errors/cfdi-pagado",
  "title": "No se puede cancelar un CFDI con pagos aplicados",
  "status": 422,
  "detail": "El CFDI 'A-1234' tiene 1 pago(s) aplicado(s) por un total de $5,800.00. Cancele primero los pagos.",
  "instance": "/api/fiscal/cfdis/{id}/cancelar",
  "traceId": "00-abc...-def...-01",
  "errores": [
    { "campo": "id", "codigo": "CFDI_PAGADO", "mensaje": "..." }
  ]
}
```

Los campos estándar (`type`, `title`, `status`, `detail`, `instance`) son los
del RFC. Los campos de extensión:

- `traceId`: el correlation ID (ADR-0006), permite que soporte busque el log exacto
- `errores`: array de errores granulares, útil para validación de formularios
  (cada uno con `campo`, `codigo`, `mensaje`)

**Mapeo de excepciones a HTTP**:

| Excepción de dominio              | Código HTTP | Notas                                        |
|-----------------------------------|-------------|----------------------------------------------|
| `ValidationException`             | 400         | Datos malformados                            |
| `BusinessRuleException`           | 422         | Regla de negocio violada                     |
| `EntityNotFoundException`         | 404         | Recurso inexistente                          |
| `ConcurrencyException`            | 409         | Conflicto optimista                          |
| `UnauthorizedAccessException`     | 401         | No autenticado                               |
| `ForbiddenException`              | 403         | Autenticado pero sin permiso                 |
| `Exception` (no manejada)         | 500         | Detalle oculto al usuario; log completo      |

**Frontend**: tipo `ApiError` en TypeScript que coincide con la forma del
Problem Details. Hook `useApiError(error)` traduce errores a mensajes de UI;
formularios mapean `errores[].campo` a sus campos para mostrar mensajes
inline.

**Errores 500**: el `detail` que se devuelve al usuario es genérico ("Ocurrió
un error inesperado. Reporta el código de seguimiento `traceId` a soporte");
el detalle real va al log estructurado y a Application Insights.

## Consecuencias

**Positivas**
- Estándar IETF: librerías y herramientas (Postman, Swagger, etc.) ya lo entienden
- Frontend tiene un solo formato a parsear
- `traceId` correlaciona errores visibles con logs internos: soporte puede investigar sin acceso al usuario
- Validación de formularios es directa: el array `errores` mapea a campos
- Errores 500 nunca filtran stacktraces ni detalles internos al usuario

**Negativas**
- Hay que escribir un `IExceptionHandler` o middleware que mapee todas las excepciones a Problem Details. Es trabajo único, no por endpoint.
- Si se agrega un tipo de excepción nuevo y se olvida mapearlo, cae a 500 genérico (mitigable con tests)

## Descartadas

**Formato propio**. Reinventar la rueda sin valor. Cualquier formato propio
que llegue a algo razonable, llega a algo casi idéntico al RFC 7807.

**JSend**. Convención popular pero menos formal que el RFC y peor soportada
por herramientas estándar.

**Excepciones serializadas tal cual**. Filtra implementación interna,
stacktraces en producción son una fuga de información.

## Notas de implementación

- Implementar middleware `ProblemDetailsMiddleware` que captura excepciones no manejadas y las traduce
- Configurar `ProblemDetailsFactory` con la extensión `traceId` automática (extraído del `Activity.Current?.TraceId`)
- Registrar `IExceptionToProblemDetailsMapper` por cada tipo de excepción de dominio
- En Swagger/OpenAPI: documentar las respuestas de error usando los `type` de Problem Details
- Frontend: definir `type ApiError = { ... }` en `frontend/src/types/api-error.ts` y exportar utilidades
- Tests: verificar que cada excepción tiene su mapeo (test parametrizado)
