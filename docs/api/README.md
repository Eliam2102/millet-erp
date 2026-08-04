# OpenAPI snapshot — drift detection

`openapi-v1.json` es la fuente de verdad **observada** del contrato HTTP
público del Millet ERP. Se commitea pretty-printed para que cualquier
cambio aparezca en `git diff` durante review.

## Cuándo regenerar

**Siempre** que un PR modifique:

- Una ruta, verbo o status code en endpoints (`backend/src/Api/Endpoints/**`)
- El shape de un request o response DTO consumido por endpoints
- Metadata `.WithSummary`, `.WithDescription`, `.Produces<T>`,
  `.ProducesProblem`, etc.
- Permisos o `[RequireIdempotencyKey]` (no aparecen en el JSON pero el
  cambio implica revisar consumidores)

## Cómo regenerar

```powershell
# Desde la raíz del repo, con Postgres dev levantado:
.\tools\openapi\regenerate-snapshot.ps1
```

El script:
1. Levanta `dotnet run` del API en `http://localhost:5050` (env=Development)
2. Espera 15s (configurable con `-WaitSeconds`)
3. Descarga `/openapi/v1.json`
4. Normaliza: elimina `servers` (env-specific), `/api/dev/*` (compilados
   `#if DEBUG`, no parte del contrato) y el path raíz `/` (Hello World)
5. Pretty-prints con indent=2
6. Sobrescribe `docs/api/openapi-v1.json`

## Cómo revisar el diff en un PR

`git diff docs/api/openapi-v1.json` muestra exactamente qué cambió en el
contrato. Cosas a observar en code review:

- **Nuevo endpoint**: confirmar permiso, idempotency key, tags, status
  codes esperados.
- **Status code agregado/quitado**: confirmar que es intencional. Quitar
  un código (ej. quitar 422 que un cliente esperaba) es **breaking**.
- **Shape de response cambiado** (props nuevos OK, props removidos /
  renombrados son **breaking**).
- **Path renombrado**: **breaking**. Considerar versión nueva
  (`/api/v2/...`) en lugar de mover en `v1`.

## Ámbito y exclusiones

El snapshot **incluye**:
- `/api/auth/**` (sesion, cambiar-empresa, me)
- `/api/v1/compras/**` (requisiciones + lineas + motivos + bandejas)
- `/api/v1/catalogos/**` (proveedores + articulos)

El snapshot **excluye** (intencional):
- `/api/dev/**` — solo compilado con `#if DEBUG`, no es parte del
  contrato productivo (ADR-0015).
- `/health/**` — operacional, no API de negocio.
- `/openapi/**`, `/scalar/**` — meta-endpoints.

## `compras-enums.contract.json` — guard de enums espejo

`compras-enums.contract.json` es el **manifiesto cross-language** de los
enums de Compras que el frontend replica a mano (objetos `as const` con
valores numéricos en `frontend/src/features/compras/**/api/types.ts` y
`schemas/`). Nació del bug de `HistoricoTipo` (el mirror del FE omitía el
sentinel `Cambio=0` del backend y corría +1 todos los valores, rotulando
mal el histórico de requisiciones).

Dos guards consumen este JSON y fallan ante cualquier drift:

- **Backend** — `EnumContractManifestTests` (`backend/tests/Compras.UnitTests/Audit/`):
  refleja sobre los 15 enums y compara por **valor numérico** contra el
  manifiesto.
- **Frontend** — `enum-contract.test.ts` (`frontend/src/features/compras/api/`):
  importa el mismo JSON y hace deep-equal contra cada objeto espejo.

### Regenerar el fixture (no editar a mano)

El manifiesto se **genera por reflexión**, nunca se edita manualmente.
Cuando cambies un enum espejo del backend, regéneralo con el toggle del
test xUnit y commitea el JSON:

```powershell
# PowerShell:
$env:UPDATE_ENUM_CONTRACT = '1'
dotnet test backend/tests/Compras.UnitTests --filter EnumContractManifestTests
Remove-Item Env:\UPDATE_ENUM_CONTRACT
```

```bash
# bash:
UPDATE_ENUM_CONTRACT=1 dotnet test backend/tests/Compras.UnitTests --filter EnumContractManifestTests
```

El toggle **no se activa en CI** (CI corre sin la variable → el test
asserta). Tras regenerar: `git diff docs/api/compras-enums.contract.json`.

### Limitación

El guard cubre solo los enums **listados** en `EnumContractManifestTests.EnumsEspejo`.
Un enum espejo nuevo debe registrarse en (1) ese arreglo, (2) el manifiesto
(regenerándolo) y (3) el guard del frontend. El fix de fondo —codegen de
tipos TS desde OpenAPI— es **ADR-0017** (follow-up, fuera de este PR).

## CI / drift check (futuro F8-PR4)

El check explícito en CI (verificar que el snapshot está en sync con el
código actual sin necesidad de levantar el API en el pipeline) queda
para F8-PR4. Mientras tanto, el reviewer es responsable de validar el
diff manualmente.

Forma del check propuesto: extraer el spec OpenAPI a build-time vía
`Microsoft.Extensions.ApiDescription.Server` y comparar con el snapshot
commiteado. Requiere que el bootstrap del API no necesite Postgres (lo
cual hoy sí — bloquea esta opción hasta que aislemos la generación).
