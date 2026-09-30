# Configuración local de Microsoft Entra ID

> Nota operativa para el vault de Obsidian del proyecto. Los identificadores de aplicación no son secretos. El `ClientSecret` nunca debe copiarse a esta nota, Git, logs ni archivos `.env`.

## Backend

Configuración efectiva esperada:

```json
{
  "Auth": {
    "Mode": "EntraId",
    "EntraId": {
      "TenantId": "15035a4e-1273-4e15-9859-68fb3fc7b033",
      "ClientId": "5be93b1d-a0b7-447e-aa87-13e70deabd5d",
      "Audience": "api://5be93b1d-a0b7-447e-aa87-13e70deabd5d",
      "SenderEmail": ""
    },
    "InitialAdminEntraOid": "6a851ba1-7d19-4132-8d2a-8e1fb152df78"
  }
}
```

Detalles:

- `Audience` no lleva espacio inicial.
- `SenderEmail` puede quedar vacío para autenticación. Sólo es obligatorio cuando se habilita el envío real mediante Microsoft Graph.
- `ClientSecret` se usa para operaciones Graph como service principal; no es necesario para que el API valide el access token del usuario.
- El usuario inicial ya existe y el bootstrap es idempotente.

### Secreto local

El secreto está registrado en .NET User Secrets para `backend/src/Api/Millet.Api.csproj`. Para reemplazarlo, ejecutar desde la raíz del repositorio:

```powershell
dotnet user-secrets set "Auth:EntraId:ClientSecret" "<SECRETO_NUEVO>" --project backend/src/Api/Millet.Api.csproj
```

No registrar el valor real en esta nota. En QA y producción debe provenir de Key Vault.

## Frontend

`frontend/.env.development.local`:

```dotenv
VITE_AUTH_MODE=EntraId
VITE_ENTRA_TENANT_ID=15035a4e-1273-4e15-9859-68fb3fc7b033
VITE_ENTRA_CLIENT_ID=5be93b1d-a0b7-447e-aa87-13e70deabd5d
VITE_API_AUDIENCE=api://5be93b1d-a0b7-447e-aa87-13e70deabd5d
VITE_ENTRA_DOMAIN=uzieltzaboutlook.onmicrosoft.com
```

El frontend convierte el audience base en el scope `api://5be93b1d-a0b7-447e-aa87-13e70deabd5d/access_as_user` al solicitar el token.

## Verificaciones realizadas

- Frontend y API iniciados en modo `EntraId`.
- `GET /health/live`: HTTP 200.
- `POST /api/auth/dev-login`: HTTP 404; el acceso falso está deshabilitado.
- Redirección de MSAL al tenant configurado observada.

## Verificación pendiente en Azure

En App Registration debe existir el scope `access_as_user` bajo el Application ID URI indicado. La Redirect URI de tipo SPA debe incluir el origen local usado por Vite, por ejemplo `http://127.0.0.1:5173/`. Completar el inicio de sesión y confirmar que `POST /api/auth/sesion` acepta el access token.
