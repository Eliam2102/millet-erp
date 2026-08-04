# ADR-0028: Ambientes de despliegue — `dev` hasta MVP, `qa-mini` y `prod` post-MVP

- **Estado**: Aceptada
- **Fecha**: 2026-05-03
- **Decisores**: Eduardo Paredes
- **Etiquetas**: infraestructura, devops, ambientes, fundación

## Contexto y problema

Hasta ahora todo el ERP corre en máquinas locales con Docker Compose
(Postgres + servicios mock). Con 15 ADRs implementadas en 6 PRs, llegó el
momento de validar las decisiones contra Azure real: connection strings con
Managed Identity, comportamiento del SignalR Service vs SignalR
self-hosted, captura real de logs en Application Insights, ejecución de
migraciones en arranque del App Service, autenticación con Entra ID en el
tenant real de Millet, etc.

Sin definir ambientes formalmente, cada decisión sobre dónde y cómo
desplegar termina siendo ad-hoc, los costos se descontrolan, y los
nombres de recursos quedan inconsistentes entre sesiones.

Pero también hay riesgo opuesto: definir tres ambientes (dev/qa/prod) hoy
cuando el equipo aún no tiene MVP genera ~$880 USD/mes de infraestructura
en su mayoría inactiva, y dos pipelines más que mantener sin que nadie los
use.

Necesitamos decidir cuántos ambientes existen hoy y cuáles se construyen
después, sin caer ni en sobre-engineering ni en improvisación.

## Drivers de la decisión

- Validar las decisiones de las ADRs 1-15 contra Azure real lo antes posible
- Mínimo costo operacional inicial mientras no se justifique más
- Estructura preparada para escalar sin rediseño cuando llegue MVP
- Trigger explícito para promoción a multi-ambiente (no postergación indefinida)
- Aislamiento estricto entre ambientes futuros (sin atajos que se vuelvan deuda)
- Naming convention consistente desde el día 1

## Opciones consideradas

1. Solo `dev` ahora; `qa-mini` y `prod` se crean al alcanzar MVP
2. Tres ambientes desde el día 1 (`dev`, `qa`, `prod`)
3. Dos ambientes desde el día 1 (`dev`, `prod`) sin QA jamás
4. `dev` + `prod` ahora, `qa-mini` cuando se justifique

## Decisión

Se adopta la **opción 1**: un solo ambiente `dev` en Azure mientras se
construye hacia MVP, con Bicep parametrizado desde el primer commit para
que agregar `qa-mini` y `prod` después sea trivial.

### Filosofía

**Construir solo el ambiente que se está usando.** Resource groups y
pipelines vacíos son deuda operacional sin beneficio. Empezar con un
ambiente y agregar el resto cuando se justifique es disciplina, no atajos.

### Estado inicial: solo `dev`

`dev` en Azure funciona como:
- Validación continua de cada PR mergeado a `main`
- Smoke testing automático post-deploy
- Validación de que la infra Bicep funciona realmente (no solo en `what-if`)
- Espacio donde el equipo descubre cómo se comportan SignalR Service, Postgres flexible, Key Vault, etc. en Azure real

**Lo que `dev` NO es**:
- NO es ambiente de demo para Millet (todavía no hay nada que demostrar formalmente)
- NO es ambiente de UAT (no hay usuarios pilot todavía)
- NO contiene datos reales de Millet
- NO es ambiente de "casi prod" — es desechable conceptualmente

### Subscripción y resource group

**Una sola Azure Subscription** para Millet. **Un solo resource group inicial**:

```
rg-millet-erp-dev
```

Cuando llegue el momento post-MVP, se crean adicionalmente:
```
rg-millet-erp-qa
rg-millet-erp-prod
```

### Naming convention de recursos

Alineado con Azure CAF (Cloud Adoption Framework):

```
{tipo}-{producto}-{ambiente}-{región-corta}-{instancia}
```

Recursos en `dev`:

| Recurso | Tipo abreviado | Nombre |
|---------|----------------|--------|
| App Service Plan | `asp` | `asp-millet-erp-dev-mxc-001` |
| App Service (API) | `app` | `app-millet-erp-api-dev-mxc-001` |
| App Service (frontend) | `app` | `app-millet-erp-web-dev-mxc-001` |
| PostgreSQL flexible server | `psql` | `psql-millet-erp-dev-mxc-001` |
| Key Vault | `kv` | `kv-millet-erp-dev-001` (KV no acepta nombres largos, máx 24 chars) |
| Storage Account | `st` | `stmilleterpdevmxc001` (sin guiones, lowercase, máx 24) |
| Application Insights | `appi` | `appi-millet-erp-dev-mxc-001` |
| SignalR Service | `sigr` | `sigr-millet-erp-dev-mxc-001` |
| Service Bus | `sb` | `sb-millet-erp-dev-mxc-001` |
| ACS Email | `acs` | `acs-millet-erp-dev-mxc-001` |
| Managed Identity | `id` | `id-millet-erp-app-dev-mxc-001` |
| Container Registry (futuro) | `cr` | `crmilleterpdevmxc001` |

**Región**: Mexico Central (`mxc`) para todos los ambientes (alineado con ADR-0006).

### Sizing de `dev`

| Recurso | Tier | Costo aprox/mes USD |
|---------|------|---------------------|
| App Service Plan | **B1** (Basic, 1 instancia) | ~$13 |
| PostgreSQL Flexible | **Burstable B1ms** (1 vCore, 2 GB) | ~$14 |
| Storage Account | **LRS** (locally redundant) | ~$2 |
| Application Insights | sampling 100% (volumen bajo) | ~$5 |
| SignalR Service | **Free** (20 conexiones) | $0 |
| Service Bus | **Basic** | <$1 |
| ACS Email | pay-as-you-go ($0.0025/email) | <$1 |
| Key Vault | Standard | <$1 |
| Backups Postgres | 7 días retention | incluido |

**Costo estimado total `dev`**: ~$60-80 USD/mes.

Estas cifras son orden de magnitud, validar con cotización real para Mexico
Central. Postgres es el grueso del gasto; el resto son centavos en `dev`
con tráfico bajo.

### Estructura de Bicep

**Parametrizada desde el primer commit** para soportar futuros ambientes
sin refactor:

```
infra/
├── main.bicep                    # entry point: recibe `environment`
├── modules/
│   ├── postgres.bicep
│   ├── app-service.bicep
│   ├── key-vault.bicep
│   ├── application-insights.bicep
│   ├── signalr.bicep
│   ├── service-bus.bicep
│   ├── storage.bicep
│   ├── managed-identity.bicep
│   └── acs-email.bicep
└── parameters/
    └── dev.bicepparam            # ÚNICO archivo de parámetros hoy
                                  # qa.bicepparam y prod.bicepparam se crean post-MVP
```

`main.bicep` recibe `environment` como parámetro (`dev` | `qa` | `prod`)
y todo el sizing/retention/redundancy se deriva de él dentro de cada
módulo. De esta forma, agregar `qa-mini` post-MVP requiere:

1. Crear `parameters/qa.bicepparam` con valores ajustados
2. Agregar workflow de pipeline apuntando a ese parámetro
3. Crear el resource group `rg-millet-erp-qa`
4. Ejecutar el deployment

Cero refactor de los módulos.

### Configuración por ambiente

**Una sola codebase, configuración inyectada**:

- **Variables de entorno** del App Service (no hardcoded en código)
- **Secrets en Key Vault** específico del ambiente
- **Parámetros de Bicep** declarados en `{ambiente}.bicepparam`

Patrón estándar:

```csharp
// Program.cs
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddAzureKeyVault(...)              // override desde KV en producción
    .AddEnvironmentVariables()           // último, gana sobre todo
    .Build();
```

### Auth en `dev` Azure

`Auth:Mode = "EntraId"` con app registration específica de `dev`. **NO**
usa el `FakeForLocalDev` de ADR-0015 — ese fake login es exclusivamente
para Docker Compose local en máquinas de desarrolladores.

Esto cierra una posible confusión:

| Contexto | Auth modo |
|----------|-----------|
| Local dev (laptop con Docker Compose) | `Auth:Mode = "FakeForLocalDev"` |
| `dev` en Azure | `Auth:Mode = "EntraId"` |
| `qa-mini` en Azure (post-MVP) | `Auth:Mode = "EntraId"` |
| `prod` en Azure (post-MVP) | `Auth:Mode = "EntraId"` |

**App registrations distintas por ambiente**: `app-reg-millet-erp-dev`,
`app-reg-millet-erp-qa`, `app-reg-millet-erp-prod`. Cada una con su
client ID, su secret en su Key Vault correspondiente.

### Datos en `dev`

- **Seed automático** con datos sintéticos al desplegar:
  - Usuarios de prueba con roles representativos
  - Empresas ficticias (ej. `Empresa Demo S.A.`, `Empresa Demo 2 S.A.`)
  - Catálogos SAT cargados (regímenes, monedas, claves de productos comunes)
  - Algunos clientes/proveedores ficticios
- **NO se resetea automáticamente** (disruptivo si alguien está debuggeando)
- Reset manual: script `scripts/reset-dev-data.sh` que cualquier dev puede ejecutar
- **Cero datos reales de Millet** en `dev`

### Dominios y URLs en `dev`

URLs por defecto de App Service:
- API: `https://app-millet-erp-api-dev-mxc-001.azurewebsites.net`
- Frontend: `https://app-millet-erp-web-dev-mxc-001.azurewebsites.net`

**No se configura custom domain en `dev`**. El dominio definitivo
(`*.millet-erp.mx` o lo que decida Millet) se evalúa cuando exista
`prod`, no antes.

### Aislamiento

`dev` está completamente aislado de los futuros `qa-mini` y `prod`:
- Resource groups distintos (cuando los demás existan)
- Subscripciones podrían ser distintas si Millet lo solicita (por ahora una sola; revaluable)
- Service principals del CI/CD distintos por ambiente con permisos `Contributor` solo al RG correspondiente
- Secrets nunca se comparten entre ambientes
- Cuando exista `prod`: datos de prod jamás se copian a dev sin anonimización

### Trigger explícito para promoción a multi-ambiente

Esta ADR establece **explícitamente** los criterios para crear `qa-mini`
y `prod`. La señal de "es momento" es la conjunción de:

1. ✅ Módulo Identidad funcional en `dev`: login con Entra real, RBAC, multi-empresa
2. ✅ Al menos un módulo de negocio operativo end-to-end (probablemente Comercial: clientes, cotizaciones, salida básica)
3. ✅ Stakeholders de Millet listos para iniciar UAT formal
4. ✅ Plan de datos iniciales para `prod`: qué catálogos SAT, qué empresas iniciales, qué usuarios admin

Cuando los cuatro se cumplan, se ejecuta el **PR de promoción a multi-ambiente**:
- Crea `parameters/qa.bicepparam` y `parameters/prod.bicepparam`
- Agrega workflows de pipeline correspondientes
- Documenta proceso de release management formal
- Despliega inicial de ambos ambientes

**Estimación temporal**: 4-8 semanas después de hoy, dependiendo del ritmo
de Fases 1-2.

### Sizing futuro de `qa-mini` y `prod`

Para que el ADR no quede ambiguo cuando llegue el momento, los tiers
recomendados son:

| Recurso | qa-mini | prod |
|---------|---------|------|
| App Service Plan | B1 (1 instancia, sin slot staging) | P1V3 (2 instancias mínimo, con deployment slots) |
| PostgreSQL | Burstable B2s, sin HA, retention 14d | GeneralPurpose D2ds_v5 con HA zonal, retention 35d, geo-redundant backup |
| Storage redundancy | LRS | ZRS |
| Application Insights | sampling 100% | sampling adaptativo (ADR-0006) |
| SignalR | Standard 1 unidad | Standard 2 unidades |
| Service Bus | Standard | Standard |
| Costo aprox/mes | ~$80 USD | ~$600-800 USD |

Estos sizings son recomendados pero pueden ajustarse en el ADR de
promoción según volumen real esperado.

### Lo que NO incluye este ADR

- **Estructura detallada de `qa-mini` y `prod`**: ADR puntual cuando se promueva. Bicep ya está parametrizado para soportarlas
- **CI/CD pipeline**: ADR siguiente (es la otra mitad del híbrido aprobado)
- **Estrategia de DR (disaster recovery)**: cuando exista `prod`
- **Multi-región**: por ahora solo Mexico Central; si surge necesidad de Mexico East como secundaria, ADR aparte
- **VNet integration y private endpoints**: empezamos con endpoints públicos + firewalls de Azure y reglas de IP allowlist; cuando seguridad de red sea requerimiento explícito, ADR aparte
- **CDN / Azure Front Door / WAF**: cuando se justifique con tráfico real
- **Análisis detallado de costos**: este ADR establece estructura, no presupuesto. Costos exactos requieren cotización formal con Microsoft México

## Consecuencias

**Positivas**
- Costo operacional inicial mínimo: ~$80/mes vs ~$880 si hubiéramos construido los tres
- Foco claro: una pipeline, un ambiente, cero distracción
- Bicep parametrizado desde día 1 hace trivial el escalado posterior (cero refactor)
- Trigger explícito para promoción evita postergación indefinida
- Validamos infra real lo antes posible: las ADRs 1-15 dejan de ser teoría
- El equipo aprende Azure operations en `dev` antes de que haya datos reales en juego
- Cero deuda técnica acumulada por construir cosas que aún no se usan

**Negativas**
- Demos al cliente antes de MVP no tienen un lugar formal: deben ser locales o en `dev` con disclaimer ("ambiente de desarrollo, no representa producción"). Aceptable para esta fase
- Cuando llegue el momento de promover, hay un PR de infra grande. Mitigado: ya está parametrizado, el contenido del PR es predecible
- El equipo nunca ejerció el flujo "deploy a qa, valida, promueve a prod" hasta MVP. Mitigado: cuando exista, se ensaya antes de tocar `prod` real
- Si Millet pide demo formal antes de MVP, hay fricción. Mitigable mostrando local con Docker Compose o el `dev` con expectativas claras

## Descartadas

**Tres ambientes desde el día 1** (`dev` + `qa` + `prod`). Tentador para
"hacer las cosas bien", pero:
- Genera ~$800/mes adicionales sin uso real
- Crea dos pipelines más que mantener cuando el equipo aún está aprendiendo Azure
- Aumenta superficie de cosas que pueden romperse sin beneficio
- Crea "ambientes zombi" donde nadie sabe cuál tiene la última versión

**Dos ambientes desde el día 1** (`dev` + `prod`). Costo más razonable
(~$700/mes) pero crea un problema operativo real:
- Demos al cliente caen en `dev` (medio-roto) o `prod` (riesgoso)
- UAT no tiene espacio claro
- Cuando llegue el momento, agregar `qa-mini` requiere haber pensado el flujo de promoción que no se ensayó

**Solo `dev` y `prod` para siempre, sin QA jamás**. Aceptable solo si el
producto fuera muy simple. ERP fiscal multi-empresa no califica:
necesita UAT formal antes de cada release importante.

**Múltiples ambientes en subscripciones distintas desde el día 1**.
Excelente para empresas con políticas estrictas de Azure governance, pero
sobre-engineering para Millet inicial. Si el cliente lo solicita después,
es ajustable.

## Notas de implementación

### Setup inicial (una vez)

1. **Confirmar subscripción de Azure** con Millet (¿usan Microsoft Customer Agreement? ¿hay convenio Empresarial? ¿quién es el Owner?)
2. **Crear el resource group** `rg-millet-erp-dev` en Mexico Central
3. **Crear app registration** `app-reg-millet-erp-dev` en el tenant de Millet (alineado con ADR-0003)
4. **Crear service principal** para CI/CD con permiso `Contributor` solo en `rg-millet-erp-dev`

### Bicep

- Estructura de carpetas como se detalla arriba
- Cada módulo recibe `environment` como parámetro
- Un decorator `@description` por cada parámetro y output (autodocumentación)
- Cada módulo expone sus IDs y nombres como `output` para que `main.bicep` los componga
- Tags estándar en todos los recursos: `Environment`, `Project`, `ManagedBy=Bicep`, `CostCenter`

Ejemplo de tag block en `main.bicep`:

```bicep
var commonTags = {
  Environment: environment
  Project: 'millet-erp'
  ManagedBy: 'Bicep'
  CostCenter: 'IT-Millet'
}
```

### Validación pre-deploy

Antes del primer despliegue real, ejecutar:

```bash
az deployment group what-if \
  --resource-group rg-millet-erp-dev \
  --template-file infra/main.bicep \
  --parameters infra/parameters/dev.bicepparam
```

Esto muestra exactamente qué se va a crear sin ejecutarlo. Validar visualmente.

### Documentación en `CLAUDE.md`

- Cómo deployar a `dev` desde local (en caso de emergencia, no es el flujo normal)
- Cómo agregar un secret nuevo a Key Vault de `dev`
- Cómo se promueve un cambio: PR → merge → CI → deploy automático a `dev`
- Cómo investigar un fallo de deploy
- Cuándo y cómo se ejecuta el PR de promoción multi-ambiente

### ADRs hijo posibles

- ADR de promoción a `qa-mini` y `prod` cuando se cumplan los triggers
- ADR de DR / business continuity cuando exista `prod`
- ADR de governance y políticas de seguridad si Millet establece marco corporativo
- ADR de FinOps / cost optimization cuando los costos se vuelvan significativos
- ADR de compliance específico (LFPDPPP, regulación CNBV si aplica)
