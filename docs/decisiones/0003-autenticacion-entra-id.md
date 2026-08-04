# ADR-0003: Autenticación con Microsoft Entra ID

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: seguridad, identidad, fundación

## Contexto y problema

El ERP requiere autenticación para todos los usuarios internos de Millet. Las
opciones razonables son: usar el directorio que el cliente ya opera (Microsoft
Entra ID, antes Azure AD), construir un sistema propio, o adoptar un proveedor
externo (Auth0, etc.). La decisión afecta la experiencia de usuario, el costo,
y el esfuerzo de mantenimiento.

Millet ya tiene tenant de Microsoft 365 con sus usuarios, grupos y políticas
de seguridad (MFA, conditional access). Cualquier opción que no aproveche eso
duplica trabajo administrativo y degrada seguridad.

## Drivers de la decisión

- SSO con la cuenta corporativa que el usuario ya usa todos los días
- MFA y políticas de acceso condicional sin reimplementarlas
- Cero costo adicional (Entra ID está incluido en M365)
- Auditoría centralizada de logins en el portal de Entra
- Lifecycle de usuarios (alta, baja, cambio de área) gestionado por TI desde un solo lugar

## Opciones consideradas

1. Microsoft Entra ID con OIDC + PKCE
2. Auth0 (o Okta) como proveedor externo
3. Sistema de autenticación propio (usuarios y contraseñas en BD del ERP)
4. Keycloak self-hosted

## Decisión

Se adopta **Microsoft Entra ID con OIDC (OpenID Connect) y flujo Authorization
Code + PKCE** para el frontend SPA. El backend valida los JWT emitidos por
Entra usando los metadatos públicos del tenant.

- Frontend: `@azure/msal-browser` y `@azure/msal-react` para login y manejo
  de tokens
- Backend: `Microsoft.Identity.Web` para validación de JWT y enriquecimiento
  del `ClaimsPrincipal`
- Los usuarios viven en Entra, no en la BD del ERP. Esta solo guarda referencias
  por `oid` (object ID de Entra) cuando necesita asociar permisos o auditoría.

## Consecuencias

**Positivas**
- SSO con M365: el usuario llega ya logueado desde Outlook, Teams, etc.
- MFA y políticas administradas por TI sin tocar código
- Sin manejo de contraseñas en el ERP: cero riesgo de fugas, cero soporte de "olvidé mi contraseña"
- Auditoría de accesos en el log de Entra ID
- Onboarding/offboarding gestionado desde un solo lugar

**Negativas**
- Acoplamiento al ecosistema Microsoft (improbable que se vaya, pero existe)
- Usuarios externos al tenant (proveedores, clientes) no son triviales: requieren B2B/B2C, decisión que se difiere a un ADR posterior si surge
- Modo desarrollo necesita un tenant de prueba o una App Registration con configuración para localhost

## Descartadas

**Auth0 / Okta**. Capacidades equivalentes pero costo adicional (~$240/mes en
adelante) y duplicación con Entra ID que el cliente ya tiene. Solo tendría
sentido si hubiera necesidad fuerte de identidades fuera del tenant corporativo.

**Sistema propio**. Implica manejar contraseñas (hashing, rotación, recuperación,
políticas de complejidad), implementar MFA, implementar reset, mantener todo
seguro. Es la opción de mayor riesgo y mayor costo. Descartada sin discusión.

**Keycloak**. Capaz pero requiere operarlo (servidor, BD, parches, alta
disponibilidad). No hay justificación cuando Entra ID está disponible.

## Notas de implementación

- Crear App Registration en el tenant de Millet (dev y prod separados)
- Configurar redirect URIs: `http://localhost:5173` (dev), URLs de App Service (qa, prod)
- Definir scopes/API: registrar la API del ERP en Entra y exponer un scope (`api://{appid}/access_as_user`)
- Frontend: configurar MSAL con el client ID y el authority del tenant
- Backend: configurar `Microsoft.Identity.Web` en `Program.cs`, agregar `[Authorize]` por defecto en controladores
- ADR-0007 (Autorización) cubre roles y permisos sobre esta base de autenticación
