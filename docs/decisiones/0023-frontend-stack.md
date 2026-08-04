# ADR-0023: Frontend stack — TanStack Query, Zustand, react-hook-form, Zod, TanStack Router

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: frontend, state, forms, routing, tier-2

## Contexto y problema

El frontend del ERP (React 19 + TypeScript + Tailwind v4 + shadcn/ui, ADR-0002)
necesita decidir varias cosas que afectan a cada componente:

- **Server state**: cómo se fetchea, cachea, invalida y sincroniza el dato del backend (clientes, listados, detalles, etc.)
- **Client state**: cómo se maneja estado local de UI que NO viene del backend (selección de empresa, modales abiertos, filtros, preferencias)
- **Forms**: cómo se construyen formularios con validación, manejo de errores, performance
- **Routing**: cómo se estructura la navegación entre pantallas
- **Validación**: cómo se duplican (responsablemente) las reglas estructurales del backend para feedback inmediato

Sin convenciones definidas, cada feature termina con su propio approach,
los componentes se vuelven incompatibles entre sí, y el onboarding de
nuevos devs es lento. Las decisiones tomadas aquí también determinan qué
patrones aplica el sistema de soft locks colaborativo (ADR-0012), el manejo
de errores `ProblemDetails` (ADR-0010) en UI, y la integración con
SignalR (ADR-0001).

## Drivers de la decisión

- TypeScript-first: las librerías deben tener tipos excelentes
- Performance: evitar re-renders innecesarios en pantallas con muchos campos o muchos datos
- Ecosistema consistente: librerías que funcionan bien juntas, no piezas de 5 ecosistemas distintos
- Integración limpia con `ProblemDetails` y errores granulares por campo (ADR-0010, ADR-0018)
- Capacidad de invalidar cache desde eventos SignalR (ADR-0001)
- Onboarding de devs: librerías populares con buena documentación
- Bundle size razonable

## Opciones consideradas

1. TanStack Query + Zustand + react-hook-form + Zod + TanStack Router (combinación moderna 2026)
2. Redux Toolkit + RTK Query + Formik + Yup + React Router (combinación clásica)
3. Solo `useState`/`useEffect` + fetch nativo (minimalista; descartado rápido)
4. Híbrido: TanStack Query + Redux Toolkit + react-hook-form

## Decisión

Se adopta la **opción 1** completa: una sola familia de librerías que
funcionan bien entre sí.

### Server state — TanStack Query

**Para todo lo que viene del backend**: listados, detalles, mutaciones.

**Capacidades clave**:
- Cache automático con stale-while-revalidate
- Invalidación granular: cuando muta algo, las queries afectadas se refetch automáticamente
- Estados `loading` / `error` / `success` listos para UI
- Devtools para debugging
- Soporte para infinite queries, paginación, optimistic updates

**Convenciones**:

- **Query keys** estructuradas: `[modulo, recurso, ...filtros]`
  ```typescript
  // listado de clientes activos en empresa actual
  ["comercial", "clientes", { activo: true }]

  // detalle de un cliente específico
  ["comercial", "clientes", clienteId]

  // CFDIs del mes actual
  ["fiscal", "cfdis", { mes: "2026-05" }]
  ```

- **Custom hooks por endpoint**, agrupados por feature:
  ```
  features/comercial/api/
  ├── useClientes.ts        # query: listado
  ├── useCliente.ts         # query: detalle
  ├── useCrearCliente.ts    # mutation
  ├── useActualizarCliente.ts
  └── useEliminarCliente.ts
  ```

- **Configuración global**:
  ```typescript
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,       // datos frescos por 30s
        gcTime: 5 * 60_000,      // cache por 5 min después de unmount
        retry: (failureCount, error) => {
          if (isProblemDetails(error) && error.status >= 400 && error.status < 500)
            return false;        // no reintentar errores 4xx
          return failureCount < 3;
        },
        refetchOnWindowFocus: false  // ERP: no necesario, los eventos SignalR cubren
      }
    }
  });
  ```

**Integración con `ProblemDetails`** (ADR-0010):

El cliente HTTP central (`api-client.ts`) detecta respuestas con
`Content-Type: application/problem+json` y lanza un `ApiError` tipado:

```typescript
class ApiError extends Error {
  constructor(public problem: ProblemDetails) {
    super(problem.title);
  }
}
```

TanStack Query captura ese `ApiError` y lo expone como `query.error` /
`mutation.error`. Los componentes muestran el mensaje desde `error.problem.detail`.

**Integración con SignalR** (ADR-0001):

Cuando un evento de hub indica que un dato cambió, se invalida la query
correspondiente:

```typescript
// En el hook de SignalR
hub.on("ClienteActualizado", (clienteId: string) => {
  queryClient.invalidateQueries({ queryKey: ["comercial", "clientes"] });
  queryClient.invalidateQueries({ queryKey: ["comercial", "clientes", clienteId] });
});
```

Las pantallas que consumen esas queries se refrescan automáticamente. Esto
es lo que da la sensación de "ERP vivo" sin que cada componente piense en
sincronización manual.

### Client state — Zustand

**Para state local que NO viene del backend**: UI state, selecciones, preferencias temporales.

**Cuándo usar Zustand**:
- Estado del selector de empresa (current, lista de disponibles, ADR-0011)
- Estado de UI: sidebar abierta/cerrada, modal activo, tema (light/dark)
- Filtros que el usuario ajusta en listados (persisten al cambiar de pantalla y volver)
- Preferencias temporales de sesión (último cliente seleccionado, etc.)

**Cuándo NO usar Zustand**:
- Datos que vienen del backend → eso es TanStack Query
- Estado local de un solo componente → `useState`
- Estado derivado simple → `useMemo`

**Convenciones**:

- Stores en `frontend/src/stores/`, uno por dominio:
  ```typescript
  // stores/empresa-store.ts
  export const useEmpresaStore = create<EmpresaState>()(
    persist(
      (set) => ({
        currentEmpresaId: null,
        empresasDisponibles: [],
        cambiarEmpresa: (id) => set({ currentEmpresaId: id }),
        setEmpresasDisponibles: (empresas) =>
          set({ empresasDisponibles: empresas })
      }),
      { name: "empresa" }  // localStorage key
    )
  );
  ```

- Selectores granulares para evitar re-renders innecesarios:
  ```typescript
  const empresaId = useEmpresaStore(s => s.currentEmpresaId);
  // NO: const store = useEmpresaStore(); store.currentEmpresaId;
  ```

- Persistencia opcional: usar `persist` middleware solo donde tiene sentido (preferencias del usuario), NO para todo (no queremos persistir state efímero)

### Forms — react-hook-form + Zod

**react-hook-form** para manejo de formularios; **Zod** para schema y validación.

**Razones**:
- **react-hook-form**: re-renders solo en campos que cambian (no todo el formulario), API simple, integración con cualquier UI library
- **Zod**: schema único define forma + validación, inferencia de tipos TypeScript desde el schema (no se duplican tipos)

**Patrón estándar**:

```typescript
import { z } from "zod";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

// Schema (frontend)
const CrearClienteSchema = z.object({
  rfc: z.string()
    .min(1, "El RFC es obligatorio")
    .regex(/^[A-ZÑ&]{3,4}\d{6}[A-Z\d]{3}$/, "Formato de RFC inválido"),
  razonSocial: z.string()
    .min(1, "La razón social es obligatoria")
    .max(254, "Máximo 254 caracteres"),
  regimenFiscal: z.string().min(1, "El régimen fiscal es obligatorio"),
  email: z.string().email("Email inválido").optional().or(z.literal(""))
});

type CrearClienteValues = z.infer<typeof CrearClienteSchema>;

function CrearClienteForm() {
  const form = useForm<CrearClienteValues>({
    resolver: zodResolver(CrearClienteSchema)
  });
  const crearCliente = useCrearCliente();

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await crearCliente.mutateAsync(values);
      // success: navegar, mostrar toast, etc.
    } catch (error) {
      if (error instanceof ApiError) {
        applyServerErrors(form, error.problem);
      }
    }
  });

  return (
    <form onSubmit={onSubmit}>
      {/* Campos con FormField de shadcn/ui */}
    </form>
  );
}
```

**Integración con backend (ADR-0018)**:

- Las **reglas estructurales** (formato de RFC, longitud, requeridos) se **duplican** en Zod para feedback inmediato sin round-trip al backend
- El duplicado es aceptable: la fuente de verdad es el backend, el frontend hace mejor UX
- Las **reglas de negocio** NO se duplican: si fallan, el backend retorna `422` con `ProblemDetails`, el frontend mapea los errores al formulario

**Helper `applyServerErrors`**:

```typescript
// lib/apply-server-errors.ts
export function applyServerErrors<T extends FieldValues>(
  form: UseFormReturn<T>,
  problem: ProblemDetails
) {
  if (!problem.errores) return;

  for (const error of problem.errores) {
    const fieldName = camelToFormField(error.campo);  // razonSocial → razonSocial
    form.setError(fieldName as Path<T>, {
      type: "server",
      message: error.mensaje
    });
  }
}
```

**Schemas reusables** (en `lib/schemas.ts`):

```typescript
export const RfcSchema = z.string()
  .min(1, "El RFC es obligatorio")
  .regex(/^[A-ZÑ&]{3,4}\d{6}[A-Z\d]{3}$/, "Formato de RFC inválido");

export const CurpSchema = z.string()
  .length(18, "La CURP debe tener 18 caracteres")
  .regex(/^[A-Z]{4}\d{6}[HM][A-Z]{5}[A-Z\d]\d$/, "Formato de CURP inválido");

export const ClabeSchema = z.string()
  .length(18, "La CLABE debe tener 18 dígitos")
  .regex(/^\d{18}$/, "La CLABE solo puede contener dígitos");

export const MonedaSchema = z.string()
  .length(3, "Código ISO 4217 de 3 caracteres");
```

Reusables: `RfcSchema` se compone en cualquier schema de formulario.

**Componentes de formulario reusables** (en `components/erp/forms/`):

- `<RfcField name="rfc" />` — wrapper sobre `FormField` de shadcn con validación de RFC
- `<MoneyField name="total" currency="MXN" />` — input de dinero con formato local
- `<DatePickerField name="fecha" />` — picker que aplica timezone (ADR-0013)
- `<EmpresaSelector />` — selector de empresa para formularios admin

Eliminan boilerplate y aseguran consistencia visual.

### Routing — TanStack Router

**Para navegación entre pantallas** del ERP.

**Capacidades clave**:
- Type-safe: rutas tipadas, params validados con Zod
- File-based routing opcional pero potente
- Integración nativa con TanStack Query (preloading de datos antes de navegar)
- Search params como state (filtros de listados van en URL automáticamente)

**Convenciones**:

- File-based routing en `routes/`:
  ```
  src/routes/
  ├── __root.tsx                  # layout raíz
  ├── _autenticado/               # rutas que requieren auth
  │   ├── _autenticado.tsx        # layout autenticado (header, sidebar)
  │   ├── inicio.tsx              # /inicio
  │   ├── identidad/
  │   │   ├── usuarios/
  │   │   │   ├── index.tsx       # /identidad/usuarios
  │   │   │   └── $id.tsx         # /identidad/usuarios/{id}
  │   │   └── roles/
  │   │       └── ...
  │   ├── comercial/
  │   │   └── clientes/
  │   │       ├── index.tsx       # /comercial/clientes
  │   │       ├── nuevo.tsx       # /comercial/clientes/nuevo
  │   │       └── $id.tsx         # /comercial/clientes/{id}
  │   └── ...
  └── login.tsx                   # /login (no requiere auth)
  ```

- Cada ruta puede declarar:
  - `loader`: precarga datos antes de renderizar (integrado con TanStack Query)
  - `validateSearch`: schema Zod para validar query params
  - `component`: el componente de la pantalla

- Search params como state: filtros de listados se reflejan en URL automáticamente, lo cual:
  - Permite compartir links a pantallas con filtros aplicados
  - Permite refresh sin perder estado
  - Permite back/forward del navegador

**Ejemplo de ruta con loader y search params**:

```typescript
// routes/_autenticado/comercial/clientes/index.tsx
const ClientesSearchSchema = z.object({
  activo: z.boolean().optional(),
  q: z.string().optional(),
  page: z.number().int().min(1).optional().default(1)
});

export const Route = createFileRoute("/_autenticado/comercial/clientes/")({
  validateSearch: ClientesSearchSchema,
  loader: ({ context }) =>
    context.queryClient.ensureQueryData(clientesQueryOptions()),
  component: ClientesPage
});
```

### Estructura de carpetas frontend

```
frontend/src/
├── lib/                          # utilities transversales
│   ├── api-client.ts             # cliente HTTP base
│   ├── api-types.ts              # generado por openapi-typescript (ADR-0017)
│   ├── api-helpers.ts            # tipos auxiliares (Schemas, Paths)
│   ├── datetime.ts               # ADR-0013: formatos, timezone
│   ├── money.ts                  # ADR-0014: formato Money
│   ├── apply-server-errors.ts    # helper para errores de validación
│   ├── schemas.ts                # schemas Zod reusables (RFC, CURP, etc.)
│   ├── query-client.ts           # TanStack Query config global
│   └── signalr.ts                # ADR-0001: cliente SignalR + hooks
├── components/
│   ├── ui/                       # primitives shadcn/ui
│   └── erp/                      # componentes de dominio
│       ├── forms/                # RfcField, MoneyField, DatePickerField, etc.
│       ├── display/              # MoneyDisplay, DateTimeDisplay, etc. (ADR-0014/0013)
│       ├── selectors/            # EmpresaSelector, ClienteSelector, etc.
│       └── collaboration/        # ConflictResolutionDialog, CollaborationIndicator (ADR-0012)
├── stores/                       # Zustand stores
│   ├── empresa-store.ts
│   ├── ui-store.ts
│   └── ...
├── features/                     # feature modules
│   ├── identidad/
│   │   ├── api/                  # custom hooks de TanStack Query
│   │   ├── components/           # componentes específicos del módulo
│   │   ├── schemas/              # Zod schemas de formularios
│   │   └── pages/                # pantallas (consumidas por routes/)
│   ├── comercial/
│   ├── fiscal/
│   └── ...
└── routes/                       # rutas TanStack Router (file-based)
    └── ...
```

**Razón de separar `features/` de `routes/`**:
- `routes/` define la navegación y consume `pages/` desde `features/`
- `features/` agrupa código de un dominio (hooks, schemas, componentes específicos)
- Permite reorganizar URLs sin tocar el código de las features

### Lo que NO incluye

- **Server-side rendering** (Next.js, Remix): no aplica, es SPA. Cache busting via Vite (ADR-0004) cubre la operación
- **Otros validators** (Yup, Joi, Valibot): Zod gana por inferencia de tipos y ecosistema
- **Otros form libraries** (Formik, Final Form): react-hook-form es superior en performance y API
- **Redux/Redux Toolkit**: descartado por boilerplate vs beneficio en este contexto
- **MobX**: válido pero solitario en el ecosistema React 2026; preferimos lo que predominantemente se usa
- **Estilizado de componentes shadcn**: ADR-0002 ya cubre eso

## Consecuencias

**Positivas**
- Stack consistente: las librerías son del mismo ecosistema (TanStack), funcionan bien juntas
- Onboarding rápido: las elecciones son las default modernas en React 2026
- Type-safe end-to-end: openapi-typescript (backend) → TanStack Query (server state) → Zod (validation) → TanStack Router (routing)
- Performance buena por default: TanStack Query maneja cache eficiente, react-hook-form minimiza re-renders
- Integración natural con SignalR para invalidaciones reactivas
- Errores de backend se manejan uniformemente (ApiError + applyServerErrors)
- Bundle size aceptable: las librerías son tree-shakeable

**Negativas**
- Curva de aprendizaje: 5 librerías nuevas para devs que vienen de Redux/Formik. Mitigado por documentación + ejemplos en el repo
- TanStack Router es más nuevo que React Router; algunas patrones aún se están consolidando. Aceptable porque la dirección del ecosistema es clara
- Disciplina obligatoria sobre cuándo usar Zustand vs TanStack Query vs `useState`. Mitigado por reglas explícitas en `CLAUDE.md`
- Schemas Zod duplican validación estructural del backend. Aceptable y necesario por UX

## Descartadas

**Redux Toolkit + RTK Query + Formik + Yup + React Router**. Stack
clásico, válido, pero en 2026 ya no es la opción más eficiente:
- Redux Toolkit es más boilerplate-heavy que Zustand para el caso de client state
- RTK Query es competente pero menos rico que TanStack Query (devtools, optimistic updates, etc.)
- Formik vs react-hook-form: react-hook-form gana en performance por mucho
- Yup vs Zod: Zod tiene mejor inferencia de tipos
- React Router es más maduro que TanStack Router pero menos type-safe

**Solo `useState` + fetch nativo**. Funciona para apps muy chicas, no para
un ERP con decenas de pantallas y datos complejos.

**Híbrido TanStack Query + Redux Toolkit**. Cargar dos sistemas de state
management para distintos propósitos no se justifica. Zustand cubre el
mismo nicho con menos overhead.

## Notas de implementación

**Setup en Fase 1**:

```bash
# Dependencias a instalar
npm install @tanstack/react-query @tanstack/react-query-devtools
npm install zustand
npm install react-hook-form @hookform/resolvers zod
npm install @tanstack/react-router @tanstack/router-devtools
```

**Configuración inicial**:
- `query-client.ts`: instancia única configurada según convenciones
- `<QueryClientProvider>` envolviendo el árbol en `main.tsx`
- `<RouterProvider router={router} />` con TanStack Router
- DevTools en desarrollo: `<ReactQueryDevtools />`, `<TanStackRouterDevtools />`

**Convenciones a documentar en `CLAUDE.md`**:
- Estructura de query keys
- Cómo escribir un custom hook de TanStack Query
- Cuándo Zustand vs TanStack Query vs `useState`
- Cómo construir un formulario con react-hook-form + Zod
- Cómo reusar schemas Zod
- Cómo invalidar queries desde eventos SignalR
- Convenciones de carpetas en `features/`

**Tests** (alineado con ADR-0016):
- Componentes con Vitest + React Testing Library
- Hooks de TanStack Query con `QueryClientProvider` envolvente
- Validación de schemas Zod: tests directos sobre los schemas
- E2E con Playwright cubre flujos completos

**Cambios en otras ADRs**:
- ADR-0002: precisar que el frontend usa TanStack ecosystem (Query + Router) además de shadcn/ui

**ADRs hijo posibles**:
- Patrón de optimistic updates para mutaciones (cuándo usarlos, cómo)
- Estrategia de paginación (cursor-based vs offset)
- Patrón de "infinite scroll" si se requiere
- Estrategia de testing visual (Storybook + Chromatic) si surge necesidad
- Internacionalización (i18n) si se agrega más adelante
