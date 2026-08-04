# PR Breakdown de frontend — Módulo Centros de Costo

> **Versión:** 0.2 · **Fecha:** 2026-07-16
> **Basado en:** [`06-frontend-plan-implementacion.md`](06-frontend-plan-implementacion.md) v0.2.

---

## 0. Cómo leer

Ramas `centros-costo-fe/pr{N}-{slug}` (✅ allowlist, auto-mode N2). Un PR
por sesión; vitest local antes de merge (el CI no lo corre para FE). El FE
es el único punto de traducción Dim↔etiqueta — helper único, no strings
sueltos ([`05-frontend-diseno.md`](05-frontend-diseno.md) §0).

---

## CECO-FE-PR1 — Foundation (S) · `centros-costo-fe/pr1-foundation` · requiere CECO-PR4

- NavCards de ambos módulos, rutas `/centros-costo/*`, guards por permisos
  (`catalogo.leer/administrar`, `asignaciones.administrar`,
  `dim3.leer-todos`).
- Mirror de permisos (post-renombre) y `EstatusCatalogo`; helper de
  traducción contextual Dim↔etiqueta.
- Árbol de configuración read-only (`ArbolCentrosCosto`, réplica de
  `ArbolSaldos`) con vocabulario "Dimensión N" y chips de grupo; mensaje
  pre-siembra.
- **DoD:** navegación completa por permisos; árbol muestra lo que haya.

## CECO-FE-PR2 — CRUD de configuración (M) · `centros-costo-fe/pr2-crud-config` · requiere CECO-PR5

- Modales por nivel con padre heredado ("Nueva Dimensión 2 en 101 —
  CONKAL"), edición (clave/nombre/grupo — nunca padre), baja con
  advertencia de cascada (conteos del backend) y reactivación.
- Toggle de inactivos, búsqueda server-side con expansión de rama, CRUD de
  "Grupo dimensión 2/3" en menú secundario.
- If-Match (428/409 → recarga con aviso); 409 con la clave en conflicto.
- **DoD:** Contabilidad opera el árbol real (361 máquinas) end-to-end.

## CECO-FE-PR3 — Asignación tri-estado (M/L) · `centros-costo-fe/pr3-asignacion` · requiere CECO-PR6

- `/centros-costo/asignaciones`: `UsuarioSelector` + **árbol de 5 niveles**
  (Dim1 → GrupoDim2 → Dim2 → GrupoDim3 → Dim3, grupos acotados al padre)
  con **checkbox tri-estado** por renglón (ninguno/parcial/todo, calculado
  por el backend) + barra resumen por dimensión.
- Marcar un nivel = POST del nodo → el backend expande a máquinas; la UI
  refresca el subárbol (no administra reglas — muestra el resultado).
- Badge "alcance total" (`dim3.leer-todos`) con árbol deshabilitado.
- **DoD:** marcar/desmarcar en cualquier nivel refleja el tri-estado
  exacto; una máquina nueva pinta "parcial" hacia arriba sin re-marcar
  (verificado contra dev).

## CECO-FE-PR4 — "Máquina" en Almacén (M) · `centros-costo-fe/pr4-maquina-almacen` · requiere CECO-PR7

- `MaquinaSelector` en `components/erp/selectors/` (molde
  `ArticuloSelector`): item `MCLC101 - Gantry` + `Corte · Conkal` + chip de
  grupo; filtrado por alcance; cold value "No catalogado".
- `NuevaSalidaSheet`: **un solo campo "Máquina" por línea** (línea con RQ y
  vale) — **se elimina la captura del campo cabecera "Máquina destino"**;
  schemas zod.
- Displays en detalle/impresión según CC-G4 resuelto para salidas.
- **DoD:** captura de salida sin GUIDs y con un solo campo; documentos
  viejos muestran "No catalogado".

## CECO-FE-PR5 — "Máquina" en Compras + barrido (M) · `centros-costo-fe/pr5-maquina-compras` · requiere CECO-PR8

- `LineaInlineForm` de RQ con `MaquinaSelector` (+ schema); displays en
  detalle RQ y flujo RQ→OC según CC-G4.
- Barrido: cero `centroCostoId` crudos en pantallas/reportes; hardening.
- **DoD:** flujo RQ end-to-end sin GUIDs; gate FE verde.

---

## CECO-FE-PR6 — Asignación de CC embebida en Usuarios (S) · `centros-costo/asignaciones-en-usuarios`

- **Post-Fase E**, 100% frontend (sin backend, sin migración). Extiende la UI
  de asignación (FE-PR3) hacia el módulo Usuarios para bajar la fricción del
  "config por demanda" de Fase E: el admin asigna alcance sin salir del
  detalle del usuario.
- Extrae `AsignacionUsuarioPanel` (`features/centros-costo/components/`) del
  cuerpo de `AsignacionCentrosCostoPage` — componente compartido con props
  `{ usuarioId: string; readOnly?: boolean }`. `ArbolAsignacion` intacto.
- Nuevo tab "Centros de Costo" en `UsuarioDetalle`
  (`modules/identidad/components/`), orden
  Datos | Roles por empresa | Centros de Costo | Preferencias, sin contador.
- **Gate:** `centros_costo.asignaciones.administrar` (el mismo del GET del
  árbol); sin permiso el tab no se renderea. Perfiles: admin con ambos
  permisos edita desde Usuarios; admin de CC puro sigue en
  `/centros-costo/asignaciones`.
- **DoD:** tab visible/oculto por permiso; pantalla existente idéntica tras el
  refactor; gate FE verde. Sin ADR (docs del módulo, §4.2 de `05`).

---

## Resumen de granularidad

| PR | Tamaño | Requiere backend |
|---|---|---|
| FE-PR1 foundation | S | CECO-PR4 |
| FE-PR2 CRUD config | M | CECO-PR5 |
| FE-PR3 asignación tri-estado | M/L | CECO-PR6 |
| FE-PR4 Máquina en Almacén | M | CECO-PR7 |
| FE-PR5 Máquina en Compras | M | CECO-PR8 |
| FE-PR6 asignación embebida en Usuarios | S | — (frontend puro) |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
| 0.2 | 2026-07-16 | Modelo Dim: dependencias renumeradas, FE-PR3 = árbol tri-estado, FE-PR4/5 = campo único "Máquina" (muere la captura de máquina destino), MaquinaSelector. |
| 0.3 | 2026-07-21 | Post-Fase E: **FE-PR6** — asignación de CC embebida en el detalle de usuario (`AsignacionUsuarioPanel` compartido + tab gateado por `asignaciones.administrar`). Frontend puro, sin ADR. |
