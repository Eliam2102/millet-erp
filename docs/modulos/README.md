# Documentación por módulo

Cada módulo del back-office tiene su propia carpeta con tres
documentos numerados:

```
docs/modulos/<modulo>/
├── 00-levantamiento-<origen>.md   ← ingeniería inversa del legacy / requerimientos
├── 01-diseno.md                   ← modelo de dominio del ERP nuevo
└── 02-plan-implementacion.md      ← fases, riesgos, sizing
```

Posteriormente pueden agregarse `03-runbook.md`, `04-post-mortem.md`,
etc. siguiendo la numeración.

## Convenciones

- **Nombres en `kebab-case`**, sin acentos en carpetas (Windows/CI).
- **Idioma español** en contenido (CLAUDE.md, regla del proyecto).
- **Sección "Dependencias de plataforma pendientes"** obligatoria en
  cada `01-diseno.md` (ver [ADR-0031](../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md)).
- **Cada documento incluye Rev.** en su sección final con resumen de
  cambios.

## Estado actual de los 10 módulos del back-office

| # | Módulo | Carpeta | 00 Levantamiento | 01 Diseño | 02 Plan |
|---|---|---|---|---|---|
| 1 | Administración (incluye Identidad y acceso, Catálogos y Datos Maestros) | [administracion/](administracion/) | ✅ | ✅ | pendiente |
| 2 | Integración A+W | — | pendiente | pendiente | pendiente |
| 3 | Facturación (CFDI 4.0) | — | pendiente | pendiente | pendiente |
| 4 | Cuentas por Cobrar | [cuentas-por-cobrar/](cuentas-por-cobrar/) | ✅ (v0.2, pendientes de owners) | ✅ borrador | ✅ borrador |
| 5 | Compras — Requisiciones | [compras-requisiciones/](compras-requisiciones/) | ✅ | ✅ | ✅ |
| 5 | Compras — Órdenes de Compra | [compras-ordenes-compra/](compras-ordenes-compra/) | ✅ | ✅ | ✅ |
| 6 | Almacén de no-producción | — | pendiente | pendiente | pendiente |
| 7 | Cuentas por Pagar | — | pendiente | pendiente | pendiente |
| 8 | Activos Fijos | — | pendiente | pendiente | pendiente |
| 9 | Contabilidad | — | pendiente | pendiente | pendiente |
| 10 | Reportes y BI | — | pendiente | pendiente | pendiente |

> Compras tiene varios submódulos. **Requisiciones** es el primero;
> seguirán Órdenes de Compra y Recepciones, cada uno en su propia
> carpeta `docs/modulos/compras-<submodulo>/` cuando se levante.

## Cómo arrancar un módulo nuevo

1. Crear `docs/modulos/<modulo>/`.
2. Producir `00-levantamiento-*.md` con la ingeniería inversa del
   legacy o los requerimientos del cliente.
3. Validar el levantamiento con el cliente y/o dueño funcional.
4. Producir `01-diseno.md` con modelo de dominio. **Incluir la
   sección "Dependencias de plataforma pendientes"** (ADR-0031).
5. Producir `02-plan-implementacion.md` con fases y sizing.
6. Levantar tickets de plataforma faltantes (si los hay) o vincular
   a tickets existentes con la convención `PLATFORM-TODO`.
7. Actualizar la tabla de arriba con los enlaces.

[Compras / Requisiciones](compras-requisiciones/) sirve como
referencia y plantilla para los siguientes módulos.
