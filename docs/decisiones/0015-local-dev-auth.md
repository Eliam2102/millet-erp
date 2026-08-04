# ADR-0015: Autenticación en desarrollo local con modo `FakeForLocalDev`

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: desarrollo, autenticación, productividad, fundación

## Contexto y problema

ADR-0003 estableció Microsoft Entra ID como mecanismo de autenticación para
todo el sistema. En producción esto es ideal: SSO con la cuenta corporativa
del usuario, MFA y políticas de acceso condicional gestionadas por TI, cero
manejo de contraseñas en el ERP.

Pero en **desarrollo local** ese mismo mecanismo introduce fricción seria
para los devs:

1. **Acceso al tenant**: cada dev necesita un usuario en algún tenant Entra. Si usan el tenant productivo de Millet, datos reales se filtran a sus máquinas locales y no pueden simular usuarios con permisos arbitrarios. Si crean usuarios falsos en producción, contaminan ese directorio.

2. **Redirect URIs**: cada App Registration de Entra declara qué URLs pueden recibir el callback de login. Localhost en distintos puertos requiere registrar cada URI, y N devs trabajando en paralelo provoca caos administrativo.

3. **Velocidad**: cada reload del frontend obliga al dev a re-autenticarse contra Microsoft (clic, password, MFA en celular). Para un workflow donde se reinicia el server 30+ veces al día, es insufrible y mata productividad.

4. **Probar permisos diversos**: probar el ERP como `Cobrador`, luego como `Auditor`, luego como `SuperAdmin` requiere tener cuentas distintas en Entra y hacer logout/login real cada vez. Imposible mantener iteración rápida así.

Necesitamos una estrategia que permita desarrollo fluido sin sacrificar la
arquitectura de auth de producción.

## Drivers de la decisión

- Productividad del dev: login local debe ser un clic, sin redirect externo, sin MFA
- Capacidad de simular distintos usuarios y roles fácilmente (varios usuarios pre-cargados)
- Imposibilidad absoluta de que el modo "dev" se filtre a QA o producción
- Compatibilidad con la opción de probar contra Entra real cuando se valida la integración
- Mantenibilidad: no agregar infraestructura paralela (otro tenant Entra que mantener) si se puede evitar

## Opciones consideradas

1. Modo híbrido: `FakeForLocalDev` por default + opción de switch a Entra real (decisión tomada)
2. Tenant productivo de Millet
3. Tenant Entra dedicado de desarrollo
4. Solo modo fake, sin posibilidad de probar Entra real localmente

## Decisión

Se adopta **opción 1: modo híbrido** con dos modos de auth controlados por
configuración:

- `Auth:Mode = "EntraId"`: validación contra el tenant Entra real (default en QA y Prod)
- `Auth:Mode = "FakeForLocalDev"`: validación de JWTs firmados localmente con clave simétrica de desarrollo (default en `appsettings.Development.json`)

### Modo `FakeForLocalDev`

**Backend**:

- Acepta JWTs firmados con clave simétrica conocida (`Auth:FakeKey` en `appsettings.Development.json`)
- Expone un endpoint `POST /api/dev/fake-login` que recibe un `oid` y devuelve un JWT firmado para esa sesión
- El endpoint solo existe cuando `Auth:Mode = "FakeForLocalDev"`; está envuelto en `#if DEBUG` y registrado condicionalmente
- En arranque, si detecta `Auth:Mode = "FakeForLocalDev"` y `ASPNETCORE_ENVIRONMENT != "Development"`, **falla con error claro**: "FakeForLocalDev no permitido fuera de Development"

**Frontend**:

- Componente `<DevUserSelector />` que se muestra cuando el frontend detecta `VITE_AUTH_MODE = "FakeForLocalDev"`
- Reemplaza el botón "Iniciar sesión con Microsoft"
- Muestra tarjetas con los usuarios de prueba pre-cargados, cada una con `[Iniciar sesión como Juan Admin]`, etc.
- Al hacer clic, llama a `POST /api/dev/fake-login` con el `oid` correspondiente, recibe el JWT, lo guarda como cualquier sesión normal

**Seed de usuarios de prueba**:

Al arrancar en modo dev, si la BD local no tiene usuarios, se ejecuta un seed
automático con 5-6 usuarios deterministas:

| oid                    | nombre              | roles asignados                           |
|------------------------|---------------------|-------------------------------------------|
| `dev-superadmin`       | Dev SuperAdmin      | `SuperAdmin` en todas las empresas seed   |
| `dev-admin-empresa-a`  | Dev Admin EmpresaA  | `Administrador` en empresa A              |
| `dev-cobrador`         | Dev Cobrador        | `Cobrador` en empresa A                   |
| `dev-facturador`       | Dev Facturador      | `Facturador` en empresa A                 |
| `dev-auditor`          | Dev Auditor         | `Auditor` con `auditoria.log.leer`        |
| `dev-multiempresa`     | Dev Multi-empresa   | Distintos roles en empresa A y empresa B  |

Los `oid`s `dev-*` son strings, no UUIDs; en producción los `oid`s reales de
Entra son GUIDs. Esta diferencia hace imposible que un usuario seed de dev se
confunda con uno real.

### Cuándo usar cada modo

- **Default diario**: `FakeForLocalDev`. El 99% del trabajo de desarrollo
- **Validar integración con Entra**: cambiar a `Auth:Mode = "EntraId"` en `.env.local` o `appsettings.Development.local.json`. Útil cuando se trabaja en flujos relacionados con Entra (cambios en MSAL, debug de claims, etc.)
- **QA y Prod**: siempre `EntraId`. El modo fake ni siquiera está disponible en builds de Release

### Garantías de seguridad

**Múltiples capas de protección para que `FakeForLocalDev` jamás llegue a un
ambiente real**:

1. **Compilación condicional**: el endpoint `/api/dev/fake-login` y el código que valida JWTs simétricos están envueltos en `#if DEBUG`. En builds de `Release` el código simplemente no existe en el binario
2. **Validación en arranque**: si `Auth:Mode = "FakeForLocalDev"` y `ASPNETCORE_ENVIRONMENT != "Development"`, la app falla con `InvalidConfigurationException` antes de aceptar requests
3. **Test de CI**: pipeline tiene un test que compila en modo Release y verifica que el endpoint `/api/dev/fake-login` retorna 404
4. **Configuración de Bicep/Key Vault**: en QA y Prod, el valor de `Auth:Mode` se inyecta desde Key Vault con valor `EntraId` hardcoded; no se puede sobrescribir vía variable de ambiente
5. **Frontend equivalente**: en builds de producción el componente `<DevUserSelector />` está bajo un `if (import.meta.env.DEV)` que el bundler elimina del bundle final

## Consecuencias

**Positivas**
- Login local es un clic, sin Entra, sin MFA, sin red externa
- Cambiar de usuario para probar permisos toma 2 segundos
- Onboarding de un nuevo dev: clonar repo, `npm install`, `dotnet run`, listo (sin coordinación con TI corporativo)
- No se contamina el tenant productivo de Millet con cuentas falsas
- No hay que mantener un tenant Entra paralelo
- Probar contra Entra real sigue siendo posible cuando se necesita

**Negativas**
- Los devs pueden olvidarse de cómo es la experiencia real de login con Entra (mitigado por testear ese flujo en QA, donde sí está activo)
- Mantener dos modos requiere disciplina para que no diverjan en comportamiento (mitigado porque ambos terminan poniendo claims equivalentes en el `ClaimsPrincipal`)
- Tests de integración deben cubrir ambos modos en algún punto del pipeline

## Descartadas

**Tenant productivo de Millet**. Riesgo alto de contaminar el directorio con
cuentas de prueba; los devs ven datos reales en sus laptops; imposible probar
permisos arbitrarios sin pedir a TI que cree usuarios; coordinación pesada
para cualquier ajuste.

**Tenant Entra dedicado de desarrollo**. Funciona pero requiere mantener un
tenant adicional: agregar/quitar devs, configurar App Registration, gestionar
políticas. Microsoft permite crear tenants gratuitos para desarrollo, pero
la fricción de "tengo que pedirle a alguien que me dé de alta" desincentiva
el uso. Y aun así no resuelve el problema de velocidad (login con MFA cada
vez).

**Solo modo fake**. Aislaría el código de la realidad de Entra; los bugs de
integración con MSAL o con claims solo aparecerían en QA, demasiado tarde.

## Notas de implementación

**Backend**

- Crear interfaz `IAuthenticationModeProvider` que retorna el modo actual
- Implementaciones: `EntraIdAuthMode` (registra `Microsoft.Identity.Web`) y `FakeAuthMode` (registra validación de JWT simétrico)
- En `Program.cs`:
  ```csharp
  if (builder.Environment.IsDevelopment() && config["Auth:Mode"] == "FakeForLocalDev")
  {
      builder.Services.AddFakeAuthForLocalDev(config);
  }
  else
  {
      builder.Services.AddEntraIdAuth(config);
      // Hard fail si alguien puso FakeForLocalDev fuera de Development
      if (config["Auth:Mode"] == "FakeForLocalDev")
          throw new InvalidConfigurationException(
              "FakeForLocalDev no está permitido fuera del ambiente Development");
  }
  ```
- Endpoint en `DevAuthController` (con `#if DEBUG`):
  ```csharp
  [HttpPost("/api/dev/fake-login")]
  public IActionResult FakeLogin([FromBody] FakeLoginRequest req)
  {
      // Genera JWT firmado con clave simétrica conocida
      // Claims: sub = req.Oid, aud = "millet-erp-dev", etc.
  }
  ```
- Seed de usuarios de prueba en `IdentidadDbContext.SeedDevUsers()` que solo corre en Development

**Frontend**

- Variable de ambiente `VITE_AUTH_MODE` (`"EntraId"` o `"FakeForLocalDev"`)
- Componente `<LoginPage />` que renderiza condicionalmente:
  - Si modo Entra: botón "Iniciar sesión con Microsoft" con flujo MSAL
  - Si modo fake: lista de tarjetas `<DevUserSelector />` con los usuarios seed
- El selector se importa con `import.meta.env.DEV ? lazy(() => import('./DevUserSelector')) : null` para que tree-shaking lo elimine del bundle de producción

**Tests de CI**
- Test de compilación en modo Release que verifica que el endpoint `/api/dev/fake-login` no existe (404 + tipo de respuesta esperado)
- Test que valida que con `Auth:Mode = "FakeForLocalDev"` y `Environment = Production`, la app no arranca

**Documentación en `CLAUDE.md`**
- Cómo arrancar el backend en modo dev (default)
- Cómo cambiar a modo Entra para validar integración
- Lista de usuarios seed disponibles y sus permisos
- Cómo agregar nuevos usuarios seed cuando se necesiten

**Cambios en otras ADRs**
- ADR-0003: nota de que en desarrollo local se usa el modo `FakeForLocalDev`; producción y QA siempre Entra ID
- ADR-0007: el seed de usuarios dev cubre el bootstrap del primer admin localmente (sin necesidad de `Auth:InitialAdminEntraOid` en `appsettings.Development.json`)

**ADRs hijo posibles**
- Si surge necesidad de un "modo demo" en QA/Prod (para presentaciones a clientes con datos pre-cargados), sería un ADR distinto con sus propias garantías
