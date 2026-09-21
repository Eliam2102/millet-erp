# 18 — Deuda de tipos en tests del frontend (hallazgo)

## Contexto

Al arreglar un error puntual de TypeScript en un smoke test de
Auditoría (`toBeInTheDocument` no reconocido por falta de un proyecto
TS que cubriera los `*.test.tsx`), se creó
`frontend/tsconfig.test.json`: un proyecto dedicado que incluye
`src/**/*.test.ts(x)` + `src/test/**`, referenciado desde
`frontend/tsconfig.json` (raíz) solo para que el editor (TS Language
Server) tenga un proyecto que cubra los tests y resuelva la
ampliación de tipos de `@testing-library/jest-dom/vitest` (declarada
en `src/test/setup.ts`).

**El build de producción no cambió de alcance.** `npm run build` ahora
corre `tsc -b tsconfig.app.json tsconfig.node.json && vite build`
explícitamente — los mismos dos proyectos que antes, sin exigir types
de `vitest`/`jest-dom`. Hay un script nuevo, `npm run typecheck:test`
(`tsc -b tsconfig.test.json`), para correr el type-check de tests a
propósito.

## Hallazgo

Los archivos `*.test.ts(x)` **nunca habían sido type-checkeados** —
`tsconfig.app.json` los excluye a propósito del build (comentario
explícito: evitar que `tsc -b` exija los types de test tooling), y no
existía ningún otro proyecto que los cubriera. `vitest run` tampoco
type-checkea (usa esbuild, solo transpila) — por eso la suite pasa en
verde (`npm test`) mientras el código tiene errores de tipos reales.

Al conectar `tsconfig.test.json`, `npm run typecheck:test` reporta
**77 errores en 44 archivos**. No bloquean el build ni la ejecución de
tests — son visibles solo como subrayado rojo en el editor y al correr
el script nuevo explícitamente.

## Desglose por código de error

| Código | Cantidad | Patrón |
|---|---|---|
| `TS2352` | 34 | Cast de `Error \| null` (tipo de error genérico de React Query) a un shape custom `{status, code?, ...}` — el cast directo falla porque `Error` no solapa con ese shape. Concentrado en `features/compras/api/*.test.tsx`, `features/facturacion/api/*.test.tsx`, `modules/*/api/*.test.tsx`. Sugiere que el tipo de error de los hooks (`useXxx`) debería tiparse como el shape de `ProblemDetails` en vez de `Error` genérico, o los tests deberían castear via `unknown` primero. |
| `TS2322` | 20 | Fixtures de test (objetos literales) que ya no calzan con el DTO/response type actual — propiedades faltantes o de tipo distinto (`centroCostoId: null` vs `string`, `tipoDocumento: number` vs enum `TipoDocumentoSerie`, etc.). Drift entre fixtures y contratos reales tras cambios de backend/DTO tipados más estrictos. |
| `TS7031` / `TS7053` | 7 | Parámetros desestructurados sin tipo explícito (`evt`, `e`) en `src/lib/hooks/useUnsavedChangesGuard.test.tsx` — `noImplicitAny` los marca. |
| `TS2739` / `TS2741` | 7 | Fixtures con propiedades requeridas faltantes (`ArticuloDetalle.categoriaId`, `ClienteDetalle.numRegIdTrib`/domicilio extranjero, `ProductoAwDetalle.fraccionArancelaria`/etc.) en los tests de idempotencia de `datos-maestros`. |
| `TS2345` | 6 | `src/features/centros-costo/lib/handle-conflict.test.ts` — fixtures de `ProblemDetails` sin la propiedad `type` (requerida por el contrato RFC 7807). |
| `TS2561` | 2 | `src/modules/datos-maestros/api/articulos.um.test.ts` — payload usa `categoria` donde el tipo real es `categoriaId` (típo/rename no propagado al test). |
| `TS2493` | 1 | `src/lib/auth/api-client.test.ts` — acceso a índice fuera de rango de una tupla vacía. |

## Archivos con más errores concentrados

- `src/lib/hooks/useUnsavedChangesGuard.test.tsx` (6)
- `src/features/compras/api/useWorkflow.test.tsx` (5)
- `src/features/centros-costo/lib/handle-conflict.test.ts` (5)
- `src/modules/identidad/api/roles.test.tsx` (3)
- `src/features/compras/ordenes/lib/acciones-disponibles.test.ts` (3)
- `src/features/compras/api/useAprobadores.test.tsx` (3)
- Resto: 1–2 errores por archivo, ver `npm run typecheck:test` para el
  listado completo y actualizado.

## Cómo reproducir

```bash
cd frontend
npm run typecheck:test
```

## Estado

**No se corrige en este ciclo** — es deuda preexistente, no relacionada
con el trabajo de F1-ADM-03, y su alcance (44 archivos en Compras,
Almacén, Facturación, Identidad, Datos Maestros, Centros de Costo,
Administración) amerita una sesión propia. Queda documentado aquí para
que quien lo tome después no tenga que redescubrirlo. El
`tsconfig.test.json` ya wireado permite iterar archivo por archivo con
`npm run typecheck:test` como criterio de cierre.
