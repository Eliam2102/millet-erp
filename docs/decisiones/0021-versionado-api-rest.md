# ADR-0021: Versionado de API REST con URL path y filosofía no-breaking

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: api, contratos, evolución, tier-2

## Contexto y problema

La API REST del ERP va a evolucionar a lo largo de su vida: nuevos
endpoints, nuevos campos, ajustes a contratos existentes. Sin política
explícita de versionado:

- Cualquier cambio puede romper al frontend o a integradores externos sin previo aviso
- Los devs no saben cuándo un cambio es "seguro" vs cuándo requiere coordinación
- Manejar versiones múltiples en paralelo se vuelve caótico si no hay convenciones
- Documentación queda desactualizada porque nadie sabe cómo declarar deprecaciones

Necesitamos definir cuándo, cómo y dónde versionar la API, qué cambios son
breaking, y cómo se coexisten múltiples versiones cuando sea necesario.

## Drivers de la decisión

- Estabilidad para el frontend: minimizar bumps de versión que requieran refactor masivo
- Flexibilidad para evolucionar: la API debe poder cambiar sin paralizarse
- Claridad para devs: convenciones simples sobre qué es breaking y qué no
- Compatibilidad con OpenAPI (ADR-0017): la spec debe poder versionarse limpiamente
- Minimizar complejidad operacional: tener N versiones activas es costoso de mantener
- Soporte para integradores externos futuros (si surgen): deprecaciones predecibles

## Opciones consideradas

1. URL path versioning + filosofía no-breaking + bump global a v2 cuando se justifique
2. Header versioning (`Accept: application/vnd.millet.v1+json`)
3. Query param versioning (`?api-version=1`)
4. Versionado por módulo (`fiscal/v2/...`, `comercial/v1/...`)
5. Sin versionado, breaking changes coordinados manualmente

## Decisión

Se adopta la **opción 1**: URL path versioning con `v1` como única versión
activa, filosofía estricta de no-breaking changes, y bump global cuando
sea inevitable.

### Estructura de URLs

```
/api/v1/identidad/usuarios
/api/v1/comercial/clientes
/api/v1/fiscal/cfdis/{id}/timbrar
```

Cuando llegue v2:

```
/api/v1/comercial/clientes  (legacy, deprecated)
/api/v2/comercial/clientes  (current)
```

**Versionado global, no por módulo**: cuando se bumpea, se bumpea TODA la
API. Razones:
- Simplifica documentación (una spec por versión, no una matriz)
- Simplifica el frontend: `api-types.ts` se genera de una sola spec
- En un ERP cuyo cliente principal es nuestro propio frontend, la complejidad de versionar por módulo no paga su costo

### Filosofía no-breaking

La API se mantiene en `v1` lo máximo posible. Los cambios que NO son
breaking (y por tanto NO requieren bump de versión):

**Cambios aditivos siempre seguros**:
- Agregar nuevos endpoints
- Agregar nuevos campos opcionales en requests (con default razonable o nullable)
- Agregar nuevos campos en responses (clientes viejos los ignoran)
- Agregar nuevos valores a un enum (con cuidado: ver más abajo)
- Agregar nuevos headers opcionales
- Agregar nuevos query params opcionales
- Relajar validaciones (lo que antes era inválido ahora se acepta)

**Cambios que parecen seguros pero NO lo son**:
- Cambiar un campo de obligatorio a opcional: PUEDE ser breaking si el cliente lo lee y asume que existe (mitigado si el cliente lo trata como nullable desde el inicio, lo cual debería ser default en TypeScript)
- Agregar valores a un enum: rompe si el cliente tiene un `switch` exhaustivo. Mitigable documentando que los enums son extensibles
- Cambiar comportamiento sin cambiar shape: si un endpoint antes devolvía resultados ordenados por fecha y ahora por relevancia, puede romper UI que asuma orden

**Cambios que SÍ son breaking** (requieren bump):
- Renombrar campos
- Eliminar campos o endpoints
- Cambiar el tipo de un campo (`string` → `int`, `nullable` → `non-nullable`)
- Cambiar HTTP status codes esperados
- Cambiar la semántica de un endpoint (mismo path, distinto comportamiento)
- Cambiar el formato de errores (mitigado por ADR-0010 que define el shape estable)
- Eliminar valores de un enum
- Hacer obligatorio un campo que era opcional

### Reglas de hygiene para evitar breaking changes

**1. Nuevos campos siempre son opcionales**:
- En requests: default razonable (`null`, `false`, `[]`) o explícitamente nullable
- En responses: clientes viejos ignoran lo que no entienden

**2. Eliminar un campo: proceso de deprecación**:
- Paso 1: marcar como `[Obsolete("Use {nuevoCampo} instead")]` en C#; agregar `deprecated: true` en la spec
- Paso 2: documentar en notas de release: "el campo X será eliminado en v2"
- Paso 3: monitorear uso (App Insights detecta requests que envían/leen el campo)
- Paso 4: cuando llegue v2, el campo se elimina

**3. Renombrar un campo: doble-write**:
- Introducir el nuevo nombre como campo adicional
- En requests: aceptar ambos; el nuevo tiene precedencia si ambos vienen
- En responses: enviar ambos durante la transición
- Marcar el viejo como `[Obsolete]`
- Eliminar el viejo en v2

**4. Cambiar el tipo de un campo: NO**:
- Si necesitas un tipo distinto, agrega un campo nuevo con otro nombre
- El campo viejo se deprecará y eliminará en v2

**5. Enum values**:
- **Agregar**: OK, pero documentar como "lista extensible". Los clientes deben tratar valores desconocidos como `unknown` o `other`, no fallar
- **Eliminar/renombrar**: breaking. Mantener el viejo como `[Obsolete]` y agregar el nuevo

**6. HTTP status codes**:
- Si un endpoint devolvía 200 y empieza a devolver 201 con location header: breaking (los clientes pueden no esperarlo)
- Si un endpoint devolvía 404 y empieza a devolver 422: breaking
- Cambios de status code requieren bump

**7. Semántica**:
- Si un cambio modifica QUÉ hace un endpoint (no cómo lo hace), es breaking
- Ejemplo: `POST /clientes` ahora también crea automáticamente una cotización vacía. Comportamiento nuevo, breaking
- Razón: clientes pueden depender del comportamiento anterior implícitamente

### Cuándo bumpear a v2

Decisión consciente, NO automática. Triggers típicos:

- Acumulación de N campos `[Obsolete]` que se vuelve incómodo mantener
- Refactor mayor de un recurso central (ej. modelo de cliente cambia significativamente)
- Cambio de paradigma (REST → GraphQL parcial; cambio de auth flow; etc.)
- Necesidad de eliminar deuda técnica acumulada

Cuando se considere bumpear, se escribe un ADR específico (`ADR-XXXX: Bump
de API a v2`) que documenta:
- Por qué se bumpea (qué cambios breaking se acumularon)
- Cuál es la diferencia v1 vs v2 (resumen)
- Plan de transición y deprecation window
- Endpoints que cambian, se eliminan, o se introducen

### Coexistencia v1 + v2

Cuando se publica v2, v1 sigue disponible pero entra en **deprecation window**:

- **Duración default**: 6 meses
- **Headers de v1 (post-bump)**:
  - `Sunset: 2026-12-31T23:59:59Z` (RFC 8594)
  - `Deprecation: true` (draft IETF)
  - `Link: </api/v2/...>; rel="successor-version"` (RFC 8631)
- **Logs y métricas**:
  - Cada request a v1 se loguea con métrica `api.deprecated_calls{version=v1, endpoint=...}`
  - Si la métrica baja a 0 sostenidamente antes de la fecha de sunset, se puede acortar
  - Si sigue alta cerca del sunset, se evalúa extender o ayudar a integradores
- **Comunicación**:
  - El frontend Millet migra a v2 inmediatamente al publicarse
  - Si hay integradores externos: notificación 60 días antes del sunset, recordatorio a los 30 y a los 7 días

### Implementación técnica

**Backend**:

- Se evalúa entre dos paquetes:
  - `Asp.Versioning.Mvc.ApiExplorer` (paquete oficial de Microsoft, fork de community Asp.Versioning)
  - `Microsoft.AspNetCore.Mvc.Versioning` (legacy, deprecated)
- **Decisión: Asp.Versioning** (la librería community moderna, mantenida)
- Configuración en `Program.cs`:
  ```csharp
  services.AddApiVersioning(options =>
  {
      options.DefaultApiVersion = new ApiVersion(1);
      options.AssumeDefaultVersionWhenUnspecified = false;  // exige versión explícita
      options.ReportApiVersions = true;
      options.ApiVersionReader = new UrlSegmentApiVersionReader();
  })
  .AddApiExplorer(options =>
  {
      options.GroupNameFormat = "'v'V";  // genera "v1", "v2"
      options.SubstituteApiVersionInUrl = true;
  });
  ```
- Controllers declaran versión:
  ```csharp
  [ApiVersion(1)]
  [Route("api/v{version:apiVersion}/comercial/clientes")]
  public class ClientesController : ControllerBase { ... }
  ```

**OpenAPI**:

- Una spec por versión: `/openapi/v1.json` (cuando exista v2: `/openapi/v2.json`)
- Scalar (UI) muestra selector de versión

**Frontend**:

- Cliente HTTP usa la versión activa (constante `API_VERSION = "v1"` en `lib/api-client.ts`)
- Cuando se migre a v2: cambia la constante, regenera tipos con `npm run codegen`, ajusta los puntos de uso que cambien
- NO se mantiene soporte simultáneo a v1 y v2 en el frontend (siempre se usa la última)

### Versionado en otros contextos (NO cubierto aquí)

Esta ADR cubre **solo el versionado de la API REST consumida por HTTP**.
Otros tipos de versionado tienen sus propias decisiones:

- **Eventos de Service Bus**: cada evento tiene su campo `version` (ADR-0009). Los consumidores manejan múltiples versiones explícitamente
- **Esquema de BD**: las migraciones SON la versión (ADR-0005). EF Core garantiza compatibilidad porque el código y el esquema se despliegan juntos
- **Frontend**: cache busting via Vite (ADR-0004); no hay versionado lógico del frontend porque es single-page app
- **SDKs externos** (cuando existan): se versionan con SemVer; cada SDK soporta UNA versión de la API

### Tests al introducir v2

Suite obligatoria de "compatibility tests" cuando se introduce v2:

- Snapshot tests con Verify de cada endpoint v1: el shape de la respuesta no cambia
- Tests que validan que clientes que envían DTOs v1 siguen funcionando contra endpoints compartidos
- Tests de coexistencia: invocar v1 y v2 del mismo recurso retorna resultados consistentes (mismo dato, distinto shape)

## Consecuencias

**Positivas**
- Estabilidad alta: la mayoría de los cambios NO requieren bump
- Frontend no se desestabiliza con cambios menores del backend
- Cuando hay bump, las reglas son claras: deprecation window, headers RFC, comunicación predecible
- OpenAPI spec versionada limpiamente (un archivo por versión)
- Path-based es la convención más conocida y debuggeable
- Bumpeo global simplifica el modelo mental

**Negativas**
- Disciplina obligatoria para no romper la API: code review debe validar que los cambios son aditivos. Mitigado por:
  - Test que carga la spec OpenAPI committeada vs la actual y detecta cambios breaking automáticamente
  - Convenciones documentadas en `CLAUDE.md`
- Acumular `[Obsolete]` en lugar de eliminar agrega ruido al código (mitigable: `Obsolete` es removible en v2)
- Coexistencia v1+v2 cuando ocurra duplica esfuerzo de mantenimiento durante el deprecation window
- Bumpeo global obliga a actualizar TODO al pasar a v2 (no solo el módulo cambiado). Aceptable como tradeoff de simplicidad

## Descartadas

**Header versioning** (`Accept: application/vnd.millet.v1+json`).
Técnicamente más "purista REST" pero menos práctico:
- Más difícil de testear con curl/Postman
- Caches HTTP suelen ignorarlo
- Logs muestran URLs idénticas para versiones distintas
- Onboarding más confuso

**Query param versioning** (`?api-version=1`). Funciona pero:
- Mezcla versionado con parámetros de query
- Caches manejan query params de forma variable
- Spec OpenAPI lo refleja como param en cada endpoint, ruidoso

**Versionado por módulo**. Más flexible pero:
- Multiplica versiones que mantener (un módulo en v3, otro en v1)
- Documentación matricial
- Frontend tendría que importar tipos de múltiples specs

**Sin versionado**. Inaceptable:
- Cualquier cambio breaking se vuelve coordinación manual de despliegues
- Imposible si surgen integradores externos
- No hay deprecation window: todo es "ahora rompe"

## Notas de implementación

**Backend**

- Agregar `Asp.Versioning.Mvc.ApiExplorer` a `Directory.Packages.props`
- Configurar `AddApiVersioning` con `UrlSegmentApiVersionReader` y default `v1`
- Convención: cada controller declara `[ApiVersion(1)]` y `[Route("api/v{version:apiVersion}/...")]`
- Cuando se introduzca v2: nuevo controller `ClientesV2Controller : ControllerBase` con `[ApiVersion(2)]`. Comparten servicios de aplicación; solo cambia el contrato HTTP

**Detección automática de breaking changes**

- En CI: descargar spec de la rama main (`/openapi/v1.json`) y compararla con la generada por la PR usando una herramienta tipo `oasdiff`:
  ```yaml
  - name: API breaking changes check
    run: |
      oasdiff breaking main-v1.json pr-v1.json --fail-on ERR
  ```
- Si detecta breaking en v1, falla CI con mensaje claro
- Excepción: PRs que introducen v2 explícitamente bypass-ean el check (con label `api-v2-bump` que requiere aprobación de architect)

**Frontend**

- Constante `API_VERSION = "v1"` en `lib/api-client.ts`; todas las URLs derivan de ahí
- Cuando se migre a v2: cambio de constante + regeneración de tipos + tests E2E

**Documentación en `CLAUDE.md`**

- Sección "API hygiene" con reglas de qué es breaking y qué no
- Cómo agregar un campo a un DTO existente (regla: opcional o nullable, default sensato)
- Cómo deprecar un campo (atributo `[Obsolete]` + nota en spec)
- Cómo proceder cuando una funcionalidad requiere cambio breaking (no hacer, agregar campo paralelo, considerar bump v2 en futuro)

**ADRs hijo posibles**

- ADR específico cuando se decida bumpear a v2 (con plan detallado)
- Política de comunicación a integradores externos (si aparecen)
- Estrategia de SDKs versionados (si se publican SDKs públicos)
