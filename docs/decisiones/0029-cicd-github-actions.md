# ADR-0029: CI/CD pipeline con GitHub Actions y deploy automático solo a `dev`

- **Estado**: Aceptada
- **Fecha**: 2026-05-03
- **Decisores**: Eduardo Paredes
- **Etiquetas**: ci-cd, devops, github-actions, fundación

## Contexto y problema

Tras 6 PRs implementados localmente y la decisión de empezar a deployar
a Azure (ADR-0028 establece solo `dev` hasta MVP), necesitamos definir
qué se ejecuta automáticamente cuando un dev push-ea código, qué se
valida antes de mergear, qué se deploya, cuándo, cómo se manejan
secrets, y bajo qué estrategia de branching.

Sin convenciones explícitas:
- Cada dev configura cosas distintas localmente
- Validaciones (lint, tests) se saltean cuando hay prisa
- El deploy a Azure se hace ad-hoc desde laptops (frágil, sin trazabilidad)
- Secrets terminan en lugares peligrosos (variables de GitHub, en código, en `.env` commiteados)

Necesitamos pipeline mínima que valide rigurosamente y deploye con
seguridad, sin sobre-engineering.

## Drivers de la decisión

- Validar PRs automáticamente: cada cambio pasa por compile + lint + tests antes de mergear
- Deploy reproducible a `dev`: imposible que "funcione en mi máquina" diverja del ambiente real
- Cero secrets de larga vida en GitHub
- Visibilidad de fallos: dev sabe inmediatamente si rompió algo
- Bajo costo operacional: pipeline simple que no requiera mantenimiento constante
- Preparado para escalar a `qa-mini` y `prod` cuando llegue el momento (sin rediseño)

## Opciones consideradas

1. GitHub Actions con dos workflows (`ci.yml`, `deploy-dev.yml`), OIDC para Azure auth
2. Azure DevOps Pipelines (descartado: el repo está en GitHub)
3. Jenkins / TeamCity self-hosted (sobre-engineering para esta etapa)
4. Deploy manual desde laptops sin pipeline (descartado: frágil, sin trazabilidad)

## Decisión

Se adopta la **opción 1**: GitHub Actions con dos workflows
(`ci.yml` y `deploy-dev.yml`), autenticación a Azure con OIDC federated
credentials, branching trunk-based simple, y migraciones EF Core
ejecutadas como step separado del pipeline.

### Plataforma: GitHub Actions

Razones sobre las descartadas:
- El repo está en GitHub: cero proveedor adicional
- Sin costo extra (incluido para repos privados con minutos generosos)
- Action oficial `azure/login` con OIDC: no se almacenan secrets de larga vida
- Workflows como código en `.github/workflows/`, versionados con el resto

### Estructura de workflows

```
.github/
└── workflows/
    ├── ci.yml           # corre en cada PR contra main
    └── deploy-dev.yml   # corre en cada merge a main
```

Cuando se promueva a multi-ambiente post-MVP (ADR-0028), se agregan:
```
    ├── deploy-qa.yml    # manual via "Run workflow"
    └── deploy-prod.yml  # manual con tag v*.*.*, requiere aprobación
```

### `ci.yml` — corre en cada PR

**Trigger**: `pull_request` contra `main`.

**Propósito**: validar que el PR no rompe el build, lint, ni tests antes
de mergear.

**Jobs** (en paralelo cuando aplique):

| Job | Propósito | Tiempo objetivo |
|------|-----------|-----------------|
| `build-backend` | `dotnet build` con todos los proyectos en Release | < 2 min |
| `build-frontend` | `npm ci && npm run build` | < 2 min |
| `lint-backend` | Roslyn analyzers, BannedApiAnalyzers (ADR-0013) | < 1 min |
| `lint-frontend` | ESLint, type-check con `tsc --noEmit` | < 1 min |
| `unit-tests-backend` | `dotnet test` proyectos `*.UnitTests` (ADR-0016) | < 1 min |
| `unit-tests-frontend` | `npm run test` con Vitest | < 1 min |
| `integration-tests` | `dotnet test` proyectos `*.IntegrationTests` con Testcontainers | < 5 min |
| `api-types-check` | Valida que `frontend/src/lib/api-types.ts` está sincronizado con la spec OpenAPI del backend (ADR-0017) | < 1 min |
| `bicep-validate` | `az deployment group what-if` contra el RG de `dev` para validar sintaxis sin ejecutar | < 1 min |

**Tiempo total objetivo**: < 10 min con paralelización.

**Política**: bloquea el merge si cualquier job falla. Sin excepciones.

### `deploy-dev.yml` — corre en cada merge a `main`

**Trigger**: `push` a `main`.

**Propósito**: desplegar el último estado de `main` a `dev` automáticamente.

**Jobs** (en serie, cada uno depende del anterior):

| Job | Propósito | Detalle |
|-----|-----------|---------|
| 1. `deploy-infra` | `az deployment group create` con Bicep + `dev.bicepparam` | Idempotente: si nada cambió, no hace nada |
| 2. `run-migrations` | Ejecuta `dotnet ef database update` contra Postgres de `dev` | Falla aquí evita que se deploye código incompatible con BD |
| 3. `deploy-backend` | Build, publish, y `azure/webapps-deploy` al App Service de la API | Health check post-deploy (`/health/ready`) |
| 4. `deploy-frontend` | Build, deploy del bundle estático al App Service del frontend | |
| 5. `smoke-tests` | Tests post-deploy: `/health`, login con usuario seed, query simple a Postgres | < 1 min; falla = rollback manual |

**Tiempo total objetivo**: < 15 min.

**Política**: si falla, alerta a Slack/Teams del equipo (configuración aparte). No hay rollback automático en esta fase: rollback manual revirtiendo el commit + redeploy.

### Migraciones EF Core: step separado en pipeline

**Decisión específica**: las migraciones se aplican como **job separado en el pipeline**, NO en el arranque de la app, NO con script SQL idempotente.

**Por qué no en arranque de la app**:
- Si dos instancias arrancan en paralelo (ej. App Service rolling deploy), race condition
- Si la migración falla, el código de la app ya está deployado pero la BD no es compatible

**Por qué no script SQL idempotente** (en esta fase):
- Útil cuando exista `prod` y necesitemos auditoría de SQL exacto que se ejecutó
- Por ahora, ejecutar `dotnet ef database update` con el código real es más simple y suficiente
- Cuando se promueva a multi-ambiente: cambiar a script idempotente para `prod` con review humana antes de aplicar

**Implementación**:

```yaml
- name: Run EF migrations
  run: |
    dotnet ef database update \
      --project backend/src/Api \
      --connection "$POSTGRES_CONNECTION_STRING"
  env:
    POSTGRES_CONNECTION_STRING: ${{ secrets.POSTGRES_CONNECTION_STRING_DEV }}
```

`POSTGRES_CONNECTION_STRING_DEV` se obtiene desde Key Vault dentro del workflow usando OIDC + Managed Identity, no se commitea en GitHub Secrets.

**Migraciones por DbContext**: si hay múltiples DbContexts (ADR-0005), el step ejecuta `dotnet ef database update` por cada uno explícitamente, en orden:

```yaml
- name: Migrate Compartido
  run: dotnet ef database update --context CompartidoDbContext ...
- name: Migrate Core
  run: dotnet ef database update --context CoreDbContext ...
- name: Migrate Identidad
  run: dotnet ef database update --context IdentidadDbContext ...
```

### Tier de tests por workflow

| Tipo de test | ¿En CI? | ¿En deploy? | Frecuencia adicional |
|--------------|---------|-------------|----------------------|
| Unit tests | Sí (PR) | No | — |
| Integration tests con Testcontainers | Sí (PR) | No | — |
| Smoke tests post-deploy | No | Sí (después de deploy) | — |
| E2E con Playwright | **No en cada PR** | No | Nightly (workflow `nightly-e2e.yml` separado, opcional, agregar cuando exista suite E2E) |

**Razón**: E2E es lento y propenso a flaky. Bloquear cada PR por E2E mata
productividad. Nightly cubre regresiones sin bloquear.

### Manejo de secrets: OIDC, no passwords

**GitHub Actions accede a Azure mediante OIDC federated credentials**, sin
secrets de larga vida en GitHub.

**Setup** (una sola vez al configurar el repo):

1. **Crear App Registration en Entra ID**: `app-reg-github-millet-erp-dev`
2. **Configurar federated credential** apuntando al repo y rama:
   - Issuer: `https://token.actions.githubusercontent.com`
   - Subject: `repo:millet/erp:ref:refs/heads/main` (para `deploy-dev.yml`)
   - Subject: `repo:millet/erp:pull_request` (para `ci.yml` con permisos de read-only)
3. **Asignar role**: `Contributor` solo sobre `rg-millet-erp-dev`
4. **Configurar GitHub Environment** `dev` con variables:
   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`
   - `AZURE_RESOURCE_GROUP`
   - `AZURE_APP_NAME_API`
   - `AZURE_APP_NAME_WEB`

Estas son IDs públicas (no son secrets), van como `vars`, no como `secrets`.

**Workflow uso típico**:

```yaml
permissions:
  id-token: write    # OBLIGATORIO para OIDC
  contents: read

steps:
  - uses: azure/login@v2
    with:
      client-id: ${{ vars.AZURE_CLIENT_ID }}
      tenant-id: ${{ vars.AZURE_TENANT_ID }}
      subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
```

**Cero passwords en GitHub Secrets** para autenticación con Azure.

### Secrets que sí necesitan vivir en algún lado

- **Connection string a Postgres** (para migraciones): en Key Vault `kv-millet-erp-dev`. El workflow lo lee usando OIDC + role Reader sobre el KV
- **Credenciales de OneFactura sandbox** (cuando aplique, futuro): mismo patrón
- **CSD certificates** (cuando aplique, futuro): nunca pasan por GitHub Actions; se administran vía Azure Portal o app del ERP

### Branching strategy: trunk-based simple

- **Una sola rama larga**: `main`
- **Feature branches cortos**: `feature/...`, `fix/...`, `docs/...`, `chore/...`
- **Vida del branch**: típicamente < 3 días; si pasa de 1 semana, sospecha rebase / split
- `main` **siempre desplegable**: si algo no está listo, no se mergea
- **Tags `v*.*.*`**: solo cuando exista `prod` para releases formales (post-MVP)

**No se usa**:
- **GitFlow** (`develop`, `release/*`, `hotfix/*`): ceremonia excesiva para esta etapa, complica el flujo de revisión
- **Branches de larga duración por desarrollador**: confuso, dificulta CI consistente

### Política de merge

- **PR mínimo 1 aprobación humana** antes de merge (no auto-merge en esta fase)
- **Status checks obligatorios**: `ci.yml` debe pasar verde
- **Squash merge** preferido: una commit limpia por PR, historia legible en `main`
- **Branch protection en `main`**:
  - No force-push
  - No eliminar
  - Required reviews: 1
  - Required status checks: todos los jobs de `ci.yml`

### Notificaciones

- **GitHub Actions UI**: estado nativo de cada workflow visible en PRs
- **Slack/Teams**: webhook que notifica al canal del equipo cuando:
  - Un deploy a `dev` falla
  - Una integración nightly E2E (cuando exista) falla
- **NO se notifica**: cada deploy exitoso (ruido innecesario)

### Observabilidad del pipeline

- Logs de cada job en GitHub Actions UI (retención 90 días)
- Application Insights captura errores post-deploy de la app (ADR-0006)
- Si surge necesidad de dashboards de métricas de pipeline: GitHub provee builtin reports; ADR aparte si se requiere más

### Validaciones de seguridad mínimas

- **Dependabot**: activado para alertar de vulnerabilidades en NuGet y npm packages
- **CodeQL**: activado para análisis estático básico (incluido en GitHub Free para repos privados)
- **Dependency review** en PRs: bloquea merges que introduzcan dependencias con CVEs altas

Estas son configuraciones del repo, no requieren código en workflows.

### Lo que NO incluye este ADR

- **Pipelines para `qa-mini` y `prod`**: ADR de promoción cuando llegue el momento (ADR-0028 lo prevé)
- **Estrategia de feature flags / dark launches**: cuando se justifique
- **Rollback automático**: por ahora rollback manual (revertir commit + redeploy)
- **Blue-green / canary deploys**: cuando exista `prod` y volumen lo justifique
- **Container deployment** (Docker images en ACR): por ahora deploy directo a App Service Linux con framework runtime; si surge necesidad de containers, ADR aparte
- **Workflow nightly E2E**: cuando exista suite E2E con Playwright (ADR-0016 lo prevé)
- **Performance testing en pipeline**: cuando se justifique
- **Monorepo concerns** (paths-filter para deploys selectivos): cuando el repo crezca, evaluar

## Consecuencias

**Positivas**
- Validación automática de cada PR: imposible mergear código que rompe build o tests
- Deploy reproducible a `dev`: cero "funciona en mi máquina"
- OIDC elimina secrets de larga vida en GitHub: cero rotación, cero exposición
- Migraciones como step separado: visibilidad clara, falla early
- Trunk-based simple: bajo overhead, ciclo de feedback rápido
- Cero costo adicional: GitHub Actions incluido en plan
- Estructura preparada para escalar a `qa-mini` y `prod` post-MVP

**Negativas**
- Setup inicial requiere configurar federated credentials en Entra ID y GitHub Environments. Mitigado: se documenta en `CLAUDE.md` paso a paso
- Tiempo de CI < 10 min asume buena paralelización; si crece más, hay que optimizar (cache de NuGet/npm, matrix strategy)
- E2E nightly puede dejar pasar regresiones por hasta 24h. Aceptable: PRs ya validan unit + integration; nightly es red de seguridad adicional
- Sin rollback automático: si un deploy a `dev` rompe algo, el equipo nota y revierte manual. Aceptable en `dev` (es el ambiente para experimentar)

## Descartadas

**Azure DevOps Pipelines**. Plataforma seria y madura, pero el repo está
en GitHub: agregaría un proveedor más (cuenta, billing, integración) sin
beneficio claro sobre GitHub Actions para nuestro caso.

**Jenkins / TeamCity self-hosted**. Más control y flexibilidad pero
sobre-engineering: hay que mantener servidor, plugins, agentes,
seguridad, backups. GitHub Actions cumple sin overhead.

**Deploy manual desde laptops sin pipeline**. Frágil:
- Cada dev tiene su propia configuración local
- Sin trazabilidad: ¿quién deployó qué versión?
- Imposible automatizar smoke tests post-deploy
- No escala cuando el equipo crece

**Workflows monolíticos con todo en uno**. Tentador (un solo archivo
`pipeline.yml`), pero mezcla responsabilidades. Separar `ci` de `deploy`
clarifica qué corre cuándo y permite re-ejecutar deploys
independientemente de PRs.

**Auto-merge tras CI verde**. Tentador para velocidad pero remueve la
revisión humana, que en esta fase es valiosa para detectar problemas que
los tests automatizados no capturan (decisiones de diseño, naming,
claridad). Cuando el equipo madure y haya patrones bien establecidos, se
puede revisar.

## Notas de implementación

### Setup inicial (una vez al configurar el repo)

1. **Crear App Registration** `app-reg-github-millet-erp-dev` en Entra ID del tenant Millet
2. **Crear federated credentials**:
   - Para deploys: subject `repo:{org}/{repo}:ref:refs/heads/main`
   - Para PRs (read-only ops): subject `repo:{org}/{repo}:pull_request`
3. **Asignar roles**:
   - `Contributor` sobre `rg-millet-erp-dev` (para crear/modificar recursos)
   - `Key Vault Secrets User` sobre `kv-millet-erp-dev-001` (para leer connection strings durante migrations)
4. **Configurar GitHub Environment `dev`** con `vars`: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, etc.
5. **Activar Branch Protection** en `main` con:
   - 1 review requerida
   - Status checks de `ci.yml` requeridos
   - No force-push
6. **Activar Dependabot** en `Settings > Code security`
7. **Activar CodeQL** con default config

### Estructura de archivos esperada

```
.github/
├── workflows/
│   ├── ci.yml
│   └── deploy-dev.yml
├── CODEOWNERS                  # define quién revisa qué (opcional inicial)
├── pull_request_template.md    # checklist para PRs
└── dependabot.yml              # config de Dependabot
infra/
├── main.bicep
├── modules/
└── parameters/
    └── dev.bicepparam
```

### Skeleton de `ci.yml`

```yaml
name: CI

on:
  pull_request:
    branches: [main]

permissions:
  contents: read
  id-token: write   # para bicep-validate con OIDC

jobs:
  build-backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore

  unit-tests-backend:
    needs: build-backend
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: dotnet test --filter "FullyQualifiedName~UnitTests" --logger "trx"

  integration-tests:
    needs: build-backend
    runs-on: ubuntu-latest
    services:
      docker:
        image: docker:dind
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: dotnet test --filter "FullyQualifiedName~IntegrationTests" --logger "trx"

  build-frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '22'
          cache: 'npm'
          cache-dependency-path: frontend/package-lock.json
      - run: npm ci
        working-directory: frontend
      - run: npm run build
        working-directory: frontend

  api-types-check:
    needs: [build-backend, build-frontend]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      # Levantar backend en background, regenerar tipos, comparar
      - name: Verify api-types.ts is in sync
        run: ./scripts/verify-api-types.sh

  bicep-validate:
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v4
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - run: |
          az deployment group what-if \
            --resource-group ${{ vars.AZURE_RESOURCE_GROUP }} \
            --template-file infra/main.bicep \
            --parameters infra/parameters/dev.bicepparam
```

### Skeleton de `deploy-dev.yml`

```yaml
name: Deploy to Dev

on:
  push:
    branches: [main]

permissions:
  contents: read
  id-token: write

jobs:
  deploy-infra:
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v4
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - run: |
          az deployment group create \
            --resource-group ${{ vars.AZURE_RESOURCE_GROUP }} \
            --template-file infra/main.bicep \
            --parameters infra/parameters/dev.bicepparam

  run-migrations:
    needs: deploy-infra
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - name: Get connection string from Key Vault
        id: get-conn
        run: |
          conn=$(az keyvault secret show \
            --vault-name kv-millet-erp-dev-001 \
            --name postgres-connection-string \
            --query value -o tsv)
          echo "::add-mask::$conn"
          echo "conn=$conn" >> $GITHUB_OUTPUT
      - name: Apply migrations
        run: |
          dotnet tool restore
          dotnet ef database update --context CompartidoDbContext \
            --project backend/src/Shared --connection "${{ steps.get-conn.outputs.conn }}"
          dotnet ef database update --context CoreDbContext \
            --project backend/src/Shared --connection "${{ steps.get-conn.outputs.conn }}"
          dotnet ef database update --context IdentidadDbContext \
            --project backend/src/Identidad --connection "${{ steps.get-conn.outputs.conn }}"

  deploy-backend:
    needs: run-migrations
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: dotnet publish backend/src/Api -c Release -o ./publish
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ vars.AZURE_APP_NAME_API }}
          package: ./publish

  deploy-frontend:
    needs: run-migrations
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
      - run: npm ci && npm run build
        working-directory: frontend
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ vars.AZURE_APP_NAME_WEB }}
          package: frontend/dist

  smoke-tests:
    needs: [deploy-backend, deploy-frontend]
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - run: |
          curl --fail https://${{ vars.AZURE_APP_NAME_API }}.azurewebsites.net/health/ready
          curl --fail https://${{ vars.AZURE_APP_NAME_WEB }}.azurewebsites.net/
```

Nota: estos skeletons son orientativos; el equipo iterará sobre ellos en
los primeros despliegues. Lo importante es la estructura general.

### Documentación en `CLAUDE.md`

- Cómo se configura un nuevo dev en el repo (clonar, instalar dotnet/node, correr local)
- Cómo se trigger-ea un deploy manualmente (push a `main` o `gh workflow run`)
- Cómo se debugea un deploy fallido (revisar logs en Actions, validar Bicep, etc.)
- Cuándo se ejecuta el PR de promoción a multi-ambiente (referencia a ADR-0028)
- Cómo se agrega un secret nuevo (Key Vault, no GitHub Secrets)

### ADRs hijo posibles

- ADR de promoción a `qa-mini` + `prod` con sus pipelines (cuando se cumplan triggers de ADR-0028)
- ADR de feature flags (cuando se justifique deploy-en-progreso de features)
- ADR de container deployment si se decide migrar de App Service framework a containers
- ADR de strategy de release / versioning cuando exista `prod`
- ADR de incident response y postmortems formales
