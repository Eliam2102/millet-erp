# Módulo Datos Maestros — placeholder

> **Estado:** placeholder. Sin diseño formal todavía.
> **Creado:** 2026-05-09 al separar el scope de catálogos cross-empresa
> del módulo Compras Requisiciones (ver
> [01-diseno.md](../compras-requisiciones/01-diseno.md) y
> [05-frontend-diseno.md](../compras-requisiciones/05-frontend-diseno.md)
> Rev. 5).

---

## Por qué existe

Los **catálogos cross-empresa** del ERP (proveedores, artículos,
sucursales, departamentos, almacenes, usuarios) son **datos
transversales**: los consumen Compras Requisiciones, Compras OC,
Cuentas por Cobrar, Cuentas por Pagar, Almacén, Activos Fijos y
Contabilidad cuando lleguen. **No pertenecen a ningún módulo de
negocio en particular**.

Anclarlos a un módulo de negocio (ej. `/compras/admin/proveedores`)
genera dos problemas:

1. **Conceptualmente raro**: un usuario de Cuentas por Cobrar
   entrando a "Compras > Admin > Proveedores" para dar de alta un
   cliente.
2. **Permisos no encajan**: `compartido.catalogos.administrar` no
   es de Compras — es transversal. Mezclar URL namespace con
   permiso namespace lleva a confusión.

Este módulo agrupa todas las pantallas de administración de
catálogos cross-empresa del ERP en un sub-árbol propio del
sidebar.

---

## Scope tentativo (a refinar cuando se diseñe formalmente)

### Pantallas de UI

| Pantalla | Ruta tentativa | Permiso | Endpoint backend |
|---|---|---|---|
| Lista + alta + edición de **proveedores** | `/datos-maestros/proveedores` | `compartido.catalogos.administrar` | ✅ `POST/PATCH/DELETE /api/v1/catalogos/proveedores/*` (PR #73 backend) |
| Lista + alta + edición de **artículos** | `/datos-maestros/articulos` | `compartido.catalogos.administrar` | ✅ `POST/PATCH/DELETE /api/v1/catalogos/articulos/*` (PR #73 backend) |
| **Reclasificar naturaleza bulk** (la antigua P10 de Compras) | `/datos-maestros/articulos/reclasificar-naturaleza` | `compartido.catalogos.administrar` | ✅ `POST /api/v1/catalogos/articulos/reclasificar-naturaleza` |
| Lista de **sucursales** (y edición/alta cuando se requiera) | `/datos-maestros/sucursales` | (a definir) | Solo GET hoy (`/api/v1/catalogos/sucursales`) |
| Lista de **departamentos** | `/datos-maestros/departamentos` | (a definir) | Solo GET hoy |
| Lista de **almacenes** | `/datos-maestros/almacenes` | (a definir) | Solo GET hoy |
| Lista de **usuarios** | `/datos-maestros/usuarios` | `identidad.usuarios.leer` (alta/edición = `identidad.usuarios.crear/editar`) | Solo GET hoy (`/api/v1/identidad/usuarios`) |

### Sidebar / shell

- Item raíz nuevo: "Datos Maestros" (o "Configuración" — decisión
  de UX). Sub-items por entidad de catálogo.
- Visibility del item raíz: `useHasAnyPermission` sobre los
  permisos de administración (típicamente solo administradores
  del ERP entran).

### Lo que NO va aquí

- Catálogos **operativos del módulo Compras** (motivos de rechazo,
  aprobadores) — esos son config local del módulo, viven bajo
  `/compras/admin/...` con permisos `compras.*`.
- Catálogos de Almacén / OC / CxP cuando lleguen y sean específicos
  del módulo — cada uno decide si vive en su `admin/` o aquí.
- Configuración de la matriz de aprobación de Compras (umbrales
  por departamento, etc.) — config de Compras, no datos maestros.

---

## Estado de dependencias backend

| Item | Estado | Referencia |
|---|---|---|
| CRUD de `compartido.proveedores` | ✅ mergeado en `main` | PR #73 |
| CRUD de `compartido.articulos` | ✅ mergeado en `main` | PR #73 |
| Reclasificar naturaleza bulk | ✅ mergeado en `main` | (PR previo, ver `CatalogosEndpoints.cs`) |
| GET de sucursales / departamentos / almacenes | ✅ mergeado en `main` | PR #70 |
| GET de usuarios | ✅ mergeado en `main` | PR #70 |
| CRUD de sucursales / departamentos / almacenes | ⏳ no existe | A pedir si/cuando se requiera administración desde el ERP |
| CRUD de usuarios (más allá de auto-provisioning Entra ID) | ⏳ a evaluar | Probable que la auto-provisión + endpoint admin existente baste |

---

## Cuándo arrancar

**No tiene plazo fijo.** Compras Requisiciones (su primer consumidor
real) sale en v1 con catálogos read-only desde sus selectores. El
módulo Datos Maestros puede arrancar **en cualquier momento**
después de UF0-PR1 de Compras (que comparten primitives shadcn,
cliente HTTP enriquecido, layout); antes se dispara overhead
duplicado.

Recomendación operativa: **arrancar el diseño formal de este
módulo (01-diseno.md, plan, breakdown) cuando UF1 de Compras esté
mergeado** — para entonces ya tendremos la plataforma del FE
estable y sabremos qué patrones reusar.

---

## Documentos a crear (cuando arranque)

- `01-diseno.md` — diseño UI (personas, flujos, pantallas, etc.).
  Mismo formato que [compras-requisiciones/05-frontend-diseno.md](../compras-requisiciones/05-frontend-diseno.md).
- `02-plan-implementacion.md` — fases con sizing.
- `03-pr-breakdown.md` — PRs concretos.

---

## Cambios

### 2026-05-09 — placeholder inicial

Creado al separar el scope del CRUD de catálogos cross-empresa del
módulo Compras Requisiciones. El backend ya mergeó los endpoints
(PR #73). El frontend del CRUD se diseñará formalmente cuando
arranque este módulo.
