# Frontend - Millet ERP

SPA en React 19 + TypeScript + Vite que consume la API de Millet ERP.

Para el contexto del proyecto completo: [../README.md](../README.md).
Arquitectura y decisiones técnicas: [../docs/arquitectura.md](../docs/arquitectura.md),
[../docs/decisiones/0023-frontend-stack.md](../docs/decisiones/0023-frontend-stack.md).

---

## Stack

- React 19 + TypeScript + Vite
- TanStack Router (file-based routing en `src/routes/`)
- TanStack Query para fetching del API
- Zustand para estado global de cliente
- react-hook-form + zod para forms y validación
- shadcn/ui + Tailwind CSS v4
- @azure/msal-react para login con Entra ID (modo prod)
- date-fns + date-fns-tz (ADR-0013)

---

## Variables de entorno

Vite carga automáticamente `.env.development` (con `npm run dev`) y
`.env.production` (con `npm run build`). Para overrides personales, copia
`.env.local.example` a `.env.local` (gitignored).

| Variable | Dev local | Producción |
|---|---|---|
| `VITE_AUTH_MODE` | `FakeForLocalDev` | `EntraId` |
| `VITE_API_BASE_URL` | `http://localhost:5000` | URL del App Service |
| `VITE_ENTRA_TENANT_ID` | (vacío) | desde Key Vault vía workflow |
| `VITE_ENTRA_CLIENT_ID` | (vacío) | desde Key Vault vía workflow |
| `VITE_API_AUDIENCE` | (vacío) | `api://<api-client-id>/access_as_user` |

En modo `FakeForLocalDev` las variables de Entra están vacías a propósito, pero
deben estar declaradas para que TypeScript no se queje (`vite-env.d.ts`).

---

## Comandos

```powershell
npm run dev       # Vite en :5173 con HMR
npm run build     # tsc -b && vite build
npm run lint      # ESLint
npm run preview   # sirve el build local en :4173
```

Equivalente vía VS Code: `Tasks: Run Task` → `frontend: dev`, `frontend: build`,
`frontend: lint`. La tarea `validate all (CI mirror)` reproduce el job de
[validate-app.yml](../.github/workflows/validate-app.yml).

---

## Login en dev local

Con `VITE_AUTH_MODE=FakeForLocalDev` el componente `<DevUserSelector />`
sustituye al botón "Iniciar sesión con Microsoft". No hay redirect a Entra
ni MFA.

Phase 1: el `BootstrapSuperAdminHostedService` del backend crea **solo el
SuperAdmin** y la empresa "Millet ERP - Empresa Inicial Dev" en el primer
arranque. ADR-0015 prevé seis usuarios seed (SuperAdmin, Admin EmpresaA,
Cobrador, Facturador, Auditor, Multi-empresa); el resto se siembra en PRs
posteriores. Mientras tanto, el `DevUserSelector` solo expone "Super Admin
(Dev)" como opción funcional.

Detalle completo: [../docs/onboarding-dev-seed-users.md](../docs/onboarding-dev-seed-users.md)
y [ADR-0015](../docs/decisiones/0015-local-dev-auth.md).

---

## Estructura

```
src/
├── routes/         ← TanStack Router file-based; `routeTree.gen.ts` se autogenera
├── features/       ← un folder por feature de negocio (vacío en Phase 1)
├── components/     ← shadcn/ui + componentes propios reutilizables
├── lib/            ← auth/, datetime, money, query-client, utils
├── stores/         ← Zustand
├── main.tsx        ← entry point
└── vite-env.d.ts   ← tipos de las VITE_* envs
```

Los aliases del bundler (`@/...`) están configurados en `vite.config.ts` y
`tsconfig.app.json`.

---

## Convenciones de código

- TypeScript estricto (`strict: true`).
- Componentes shadcn se generan con `npx shadcn add <componente>` y caen en
  `src/components/ui/`. No los edites a mano salvo para tweaks pequeños — si
  necesitas variantes, envuelve el componente en otro propio.
- Tailwind v4: tokens y theme se configuran vía CSS (`@theme` en
  `src/index.css`), no via `tailwind.config.ts`.
- Llamadas al API se hacen siempre con TanStack Query, no con `useEffect` +
  `fetch` directo.
- Forms usan `useForm` + `zodResolver` para validación.

---

## Testing

Pendiente (ver [ADR-0016](../docs/decisiones/0016-estrategia-testing.md) y
TODO en [validate-app.yml](../.github/workflows/validate-app.yml)). En
Phase 1 no hay suite de Playwright todavía; se agrega cuando lleguen los
módulos de negocio.
