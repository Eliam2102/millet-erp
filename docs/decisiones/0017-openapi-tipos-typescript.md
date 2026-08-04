# ADR-0017: Documentación de API con OpenAPI nativo y generación de tipos TypeScript

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: api, frontend, contratos, tier-2

## Contexto y problema

El backend expone una API REST que el frontend (y eventualmente
integraciones externas) consume. Sin un contrato formal entre ambos:

- El frontend escribe tipos TypeScript a mano que se desincronizan del backend silenciosamente
- Cambios en DTOs del backend rompen el frontend solo en runtime, sin error de compilación
- Devs nuevos no saben qué endpoints existen ni qué reciben/devuelven sin leer código del backend
- Integradores externos (futuros consumidores) no tienen referencia confiable

Necesitamos una spec de API que sea:

- Generada automáticamente desde el código del backend (no escrita a mano: se desincroniza)
- Consumible como tipos TypeScript en el frontend (cero código duplicado)
- Visible vía UI interactiva en desarrollo
- No expuesta en producción (los endpoints internos no necesitan documentación pública)

## Drivers de la decisión

- Sincronía garantizada entre tipos del backend y del frontend (rompe build si divergen)
- Cero mantenimiento manual de documentación de API
- UI explorable en dev para que el equipo entienda los endpoints sin leer C#
- Bundle del frontend liviano: tipos generados, no clientes runtime
- Alineación con la dirección oficial de Microsoft en .NET 9

## Opciones consideradas

1. `Microsoft.AspNetCore.OpenApi` nativo + `openapi-typescript` para tipos en frontend
2. Swashbuckle (Swagger.AspNetCore) + Swagger UI + `openapi-typescript`
3. NSwag (genera spec + cliente C# y TypeScript en un solo paso)
4. tRPC u otra alternativa no-OpenAPI
5. Documentación manual en Markdown

## Decisión

Se adopta la **opción 1**: `Microsoft.AspNetCore.OpenApi` (nativo de .NET 9)
para generar la spec, **Scalar** como UI interactiva en desarrollo, y
**`openapi-typescript`** en el frontend para generar tipos.

### Backend: generación de la spec

- Paquete: `Microsoft.AspNetCore.OpenApi` (nativo desde .NET 9, reemplaza a Swashbuckle como recomendación oficial)
- Spec disponible en `/openapi/v1.json` automáticamente, generada desde los endpoints + atributos
- Configuración en `Program.cs`:
  ```csharp
  builder.Services.AddOpenApi("v1", options =>
  {
      options.AddDocumentTransformer((document, context, ct) =>
      {
          document.Info.Title = "Millet ERP API";
          document.Info.Version = "v1";
          // Agregar componentes de seguridad para JWT
          return Task.CompletedTask;
      });
  });

  if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
  {
      app.MapOpenApi();
      app.MapScalarApiReference();  // UI en /scalar/v1
  }
  ```

### UI interactiva en desarrollo

- **Scalar** en lugar de Swagger UI clásico
- Razones: más rápido en cargar, mejor UI, soporta dark mode nativo, OpenAPI 3.1 completo, gratis para self-host
- Endpoint: `/scalar/v1`
- **No se expone en producción**: bloqueado por configuración (`if Environment.IsDevelopment() || IsStaging()`)
- Si se requiere documentación pública en el futuro (integradores externos), se evalúa exponer la spec con auth (no la UI completa)

### Convenciones para que la spec sea útil

Cada endpoint debe declarar de forma explícita:

- **Tipo de respuesta exitosa**: `[ProducesResponseType<ClienteDto>(StatusCodes.Status200OK)]`
- **Errores esperados**: `[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]`, `Status404`, `Status409`, `Status422`, alineado con ADR-0010
- **Tag por módulo**: `[Tags("Identidad")]`, `[Tags("Comercial")]`, etc. — agrupa endpoints en la UI
- **Summary y description** vía XML doc comments:
  ```csharp
  /// <summary>Crea un cliente nuevo en la empresa actual.</summary>
  /// <remarks>El RFC debe ser único por empresa. Si ya existe, retorna 409.</remarks>
  ```
- **Parámetros documentados**: `[FromQuery, Description("Filtro por estatus")] string? estatus`

### Estructura de DTOs

- DTOs son `record`s en `Api/Contracts/{Modulo}/`, separados de entidades de dominio
- Razón: las entidades cambian por razones de dominio; los DTOs cambian por razones de API. Mantenerlos separados evita acoplamiento entre capas
- **Naming consistente**:
  - `Crear{Recurso}Request` para body de POST
  - `Actualizar{Recurso}Request` para body de PUT/PATCH
  - `{Recurso}Response` o `{Recurso}Dto` para respuestas
  - `{Recurso}ListItemDto` para items de listados (suele ser un subset de `{Recurso}Dto`)
- Mappers explícitos entre Domain ↔ DTO (no AutoMapper en esta fase; mappers a mano son más predecibles y debuggeables)

### Frontend: generación de tipos TypeScript

- Herramienta: **`openapi-typescript`** (no `openapi-generator`, no NSwag)
- Output: archivo único `frontend/src/lib/api-types.ts` con todos los tipos del schema
- **No genera clientes**: solo tipos. Cero runtime dependency en el frontend
- Setup:
  ```json
  // frontend/package.json
  "scripts": {
    "codegen": "openapi-typescript http://localhost:5000/openapi/v1.json --output src/lib/api-types.ts"
  }
  ```

### Cliente HTTP del frontend

- **Implementado a mano** con `fetch` (o `ky` si surge necesidad de retry/timeout más sofisticado), **tipado con los tipos generados**
- NO se genera SDK completo. Razones:
  - Los SDKs de `openapi-generator` son verbose, opinionados y traen clases/factories que no necesitamos
  - Control directo del cliente nos permite manejar `Authorization`, `current_empresa_id`, retries, errores con `ProblemDetails` de forma uniforme y a nuestra manera
  - Menos código en el bundle final

- Estructura del cliente HTTP:
  ```typescript
  // frontend/src/lib/api-client.ts
  import type { components } from "./api-types";

  type ProblemDetails = components["schemas"]["ProblemDetails"];

  export const api = {
    get: <T>(url: string) => http<T>("GET", url),
    post: <T>(url: string, body?: unknown) => http<T>("POST", url, body),
    // ...
  };
  ```

- Helpers de tipos para uso ergonómico:
  ```typescript
  // frontend/src/lib/api-helpers.ts
  import type { components, paths } from "./api-types";

  export type Schemas = components["schemas"];
  export type Paths = paths;
  ```

- Uso en componentes:
  ```typescript
  import type { Schemas } from "@/lib/api-helpers";

  type Cliente = Schemas["ClienteDto"];
  type CrearClienteRequest = Schemas["CrearClienteRequest"];

  async function crearCliente(req: CrearClienteRequest): Promise<Cliente> {
    return api.post<Cliente>("/api/comercial/clientes", req);
  }
  ```

### Workflow de desarrollo

1. Dev cambia un endpoint o DTO en el backend
2. Levanta el backend localmente
3. Corre `npm run codegen` en `frontend/`
4. Los tipos generados se actualizan en `api-types.ts`
5. TypeScript compila el frontend; si algo no encaja, falla en build con error claro
6. Dev arregla los puntos de uso, commitea ambos cambios juntos

### Validación en CI

- Job de CI: levanta el backend en modo test, descarga la spec, regenera tipos, compara con el archivo commiteado
- Si difieren, falla el build con mensaje:
  ```
  ERROR: api-types.ts está desactualizado. Corre 'npm run codegen' y commitea.
  Diff detectado en: ClienteDto.email (string -> string | null)
  ```
- Esto fuerza que cualquier cambio del backend que afecte la API venga con sus tipos del frontend actualizados

### Versionado de la spec

- La spec versiona: `/openapi/v1.json`
- Cuando se rompa compatibilidad, será `/openapi/v2.json` y los endpoints v2 vivirán en `/api/v2/*`
- La estrategia de **cuándo bumpear v1→v2** es decisión separada (ADR de versionado de API, pendiente en backlog Tier-2)
- Esta ADR solo deja la mecánica preparada para que soporte multi-versión sin retrabajo

### Seguridad de la spec en producción

- En producción la UI de Scalar **no se expone** (bloqueada por configuración de ambiente)
- La spec JSON tampoco se expone públicamente: si en el futuro hay integradores externos, se evaluará un endpoint con auth o un portal de developers separado
- El bundle del frontend NO incluye `api-types.ts` con info sensible (los tipos son shape, no secrets); este archivo se commitea al repo y se distribuye normalmente

## Consecuencias

**Positivas**
- Sincronía automática entre backend y frontend: imposible que diverjan sin error de build
- Documentación viva: cualquier cambio en código se refleja en la spec sin trabajo extra
- Onboarding más rápido: dev nuevo abre `/scalar/v1` y ve toda la API explorable
- Control total del cliente HTTP: no peleamos contra abstracciones generadas
- Bundle frontend mínimo: solo tipos, cero runtime de SDK
- Alineado con dirección oficial de Microsoft en .NET 9

**Negativas**
- Disciplina obligatoria: cada endpoint debe declarar `[ProducesResponseType]`, tags, etc. Si el dev olvida, la spec queda incompleta. Mitigado por convención y revisión en PR
- Mappers Domain ↔ DTO se escriben a mano: boilerplate, pero predecibles. Si crece mucho, se evalúa Mapperly (source-generator) en futuro ADR
- `npm run codegen` requiere backend levantado: pequeña fricción, pero es momentánea
- Scalar es relativamente nuevo (2024+): si surgen bugs, fallback a Swagger UI clásico es trivial

## Descartadas

**Swashbuckle (Swagger.AspNetCore)**. Funcional y maduro, pero Microsoft
está empujando `Microsoft.AspNetCore.OpenApi` como sucesor oficial en .NET
9. Adoptar Swashbuckle ahora sería empezar con tecnología en camino a
deprecación. La generación nativa también es más rápida en build.

**NSwag**. Genera spec + cliente en un paso, lo cual suena conveniente. Pero
genera SDKs verbose con clases por endpoint, fábricas de DI, y opciones que
no necesitamos. La separación spec / tipos / cliente nos da más control.

**tRPC u alternativas no-OpenAPI**. Excelente para proyectos full-stack
TypeScript donde frontend y backend comparten código. No aplica:
backend es .NET, OpenAPI es el estándar de facto cross-stack.

**Documentación manual en Markdown**. Se desincroniza inmediatamente del
código. Es escribir documentación que envejece mal. Cero retorno.

**`openapi-generator`** (alternativa a `openapi-typescript`). Genera SDKs
completos con clientes, modelos, configs. Demasiado opinionado y verboso
para nuestro caso. `openapi-typescript` solo genera tipos, que es justo lo
que queremos.

## Notas de implementación

**Backend**

- Agregar paquete `Microsoft.AspNetCore.OpenApi` y `Scalar.AspNetCore` a `Directory.Packages.props`
- Configuración en `Program.cs` (ver snippet arriba)
- Habilitar XML doc comments en cada `.csproj`:
  ```xml
  <PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn>  <!-- evitar warnings por miembros sin doc -->
  </PropertyGroup>
  ```
- Crear estructura `Api/Contracts/{Modulo}/` por módulo (en Fase 2 cuando aparezca el primer módulo de negocio; en Fase 1 solo el esqueleto)
- Convención: cada endpoint declara explícitamente sus `[ProducesResponseType]`; un test verifica que todos los endpoints tienen al menos `200` y `400` declarados

**Frontend**

- Agregar `openapi-typescript` como devDependency
- Script `codegen` en `package.json` apuntando a la URL local del backend
- Crear `frontend/src/lib/api-client.ts` con el cliente HTTP base
- Crear `frontend/src/lib/api-helpers.ts` con tipos auxiliares (`Schemas`, `Paths`)
- Configurar `tsconfig.json` para que `api-types.ts` se incluya en el path resolver

**CI**

- Job `verify-api-types`:
  1. Levanta backend en modo test
  2. Descarga `openapi/v1.json`
  3. Regenera `api-types.ts` en una carpeta temporal
  4. Compara con el `api-types.ts` commiteado
  5. Si difieren, falla el job con mensaje claro y muestra diff

**Documentación en `CLAUDE.md`**

- Cómo agregar un endpoint nuevo (atributos requeridos, naming de DTOs, tags)
- Cómo regenerar tipos en el frontend
- Cómo manejar errores con `ProblemDetails` desde el frontend (alineado con ADR-0010)
- Convenciones de naming de DTOs

**ADRs hijo posibles**

- Estrategia de versionado de API (cuándo bumpear v1→v2)
- Adopción de Mapperly para mappers Domain ↔ DTO si el boilerplate crece
- Estrategia de mock server desde la spec (Prism u otro) si se necesita para tests E2E sin backend levantado
- Política de exposición de spec a integradores externos (si surge la necesidad)
