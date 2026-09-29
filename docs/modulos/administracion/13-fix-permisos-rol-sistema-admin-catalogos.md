---
title: Fix Permisos del Rol del Sistema admin-catalogos y admin-datos-maestros
tags:
  - administracion
  - identidad
  - rbac
  - catalogos
  - fix
  - f1-adm-04
date: 2026-09-28
author: Uziel Tzab / Antigravity
status: Propuesta / Plan Técnico
---

# Fix Permisos del Rol del Sistema `admin-catalogos` y `admin-datos-maestros`

> [!IMPORTANT] Resumen del Problema
> En la sección de **Roles y Permisos**, el rol del sistema **`admin-catalogos`** (*Administrador de Catálogos*) tiene la etiqueta de **Sistema** (`esDelSistema = true`), lo cual deshabilita la edición manual de sus permisos en la interfaz. Actualmente solo tiene asociados 2 permisos (`compartido.catalogos.leer` y `compartido.catalogos.administrar`), mientras que los 6 permisos específicos del módulo **Catálogos** (`catalogos.*.gestionar`) están desmarcados (0/6).
>
> Esto provoca que usuarios asignados a este rol (como la cuenta de prueba de QA de Joaquín) solo vean 4 catálogos de consulta general en la pantalla de Administración en lugar de las 11 tarjetas correspondientes a la gestión de catálogos compartidos.

---

## 1. Diagnóstico Técnico

### 1.1 Causa Raíz
En el archivo [`backend/src/Identidad/Infrastructure/BootstrapSuperAdminHostedService.cs`](file:///c:/Users/uzieltzab/Documents/For_job/vilo_ai_studio/millet-erp/backend/src/Identidad/Infrastructure/BootstrapSuperAdminHostedService.cs#L209-L222), el predicado que filtra qué permisos pertenecen al rol `admin-catalogos` se definió de forma restrictiva:

```csharp
(
    Guid.Parse("00000002-0003-0000-0000-000000000004"),
    "admin-catalogos",
    "Administrador de Catálogos",
    "CRUD de catálogos cross-empresa (monedas, condiciones, SAT, etc.).",
    p => p.Codigo.StartsWith("compartido.catalogos", StringComparison.Ordinal)
),
(
    Guid.Parse("00000002-0003-0000-0000-000000000005"),
    "admin-datos-maestros",
    "Administrador de Datos Maestros",
    "Gestión de proveedores y artículos (catálogos operativos).",
    p => p.Codigo.StartsWith("compartido.catalogos", StringComparison.Ordinal)
)
```

Cuando se diseñó este seed inicialmente, todos los catálogos residían bajo el prefijo `compartido.catalogos.*`. Sin embargo, en fases posteriores se introdujeron permisos granulares para los catálogos especializados:

* **Módulo Catálogos:**
  * `catalogos.monedas.gestionar`
  * `catalogos.tipos-cambio.gestionar`
  * `catalogos.condiciones-pago.gestionar`
  * `catalogos.incoterms.gestionar`
  * `catalogos.transportistas.gestionar`
  * `catalogos.unidades-medida.gestionar`
* **Módulo Datos Maestros:**
  * `datos_maestros.proveedores.gestionar`
  * `datos_maestros.articulos.gestionar`
  * `datos_maestros.clientes.gestionar`
  * `datos_maestros.productos_aw.gestionar`

Al omitir estos prefijos en los predicados del hosted service, el rol fue sembrado sin ellos, y la UI bloquea su corrección manual por ser rol protegido del sistema.

---

## 2. Plan de Solución Técnica

### 2.1 Actualización de Predicados en `BootstrapSuperAdminHostedService.cs`
Modificar la definición declarativa de ambos roles para incluir sus respectivos prefijos granulares:

```csharp
(
    Guid.Parse("00000002-0003-0000-0000-000000000004"),
    "admin-catalogos",
    "Administrador de Catálogos",
    "CRUD de catálogos cross-empresa (monedas, condiciones, SAT, etc.).",
    p => p.Codigo.StartsWith("compartido.catalogos", StringComparison.Ordinal)
         || p.Codigo.StartsWith("catalogos.", StringComparison.Ordinal)
),
(
    Guid.Parse("00000002-0003-0000-0000-000000000005"),
    "admin-datos-maestros",
    "Administrador de Datos Maestros",
    "Gestión de proveedores y artículos (catálogos operativos).",
    p => p.Codigo.StartsWith("compartido.catalogos", StringComparison.Ordinal)
         || p.Codigo.StartsWith("datos_maestros.", StringComparison.Ordinal)
)
```

### 2.2 Sincronización Automática e Idempotente
El método `EnsureRolesMvpAsync` ya implementa la lógica de reconciliación:
```csharp
var faltantes = permisosEsperados.Except(existingPermisoIds).ToList();
if (faltantes.Count > 0)
{
    foreach (var permisoId in faltantes)
    {
        db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rol.Id, permisoId));
    }
    await db.SaveChangesAsync(cancellationToken);
}
```
Por lo tanto, no se requiere ninguna migración de esquema en la base de datos ni scripts SQL manuales: al arrancar el API, el servicio detectará los permisos faltantes y los insertará automáticamente en `identidad.rol_permisos`.

### 2.3 Pruebas Automatizadas de Integración
Actualizar [`BootstrapRolesMvpTests.cs`](file:///c:/Users/uzieltzab/Documents/For_job/vilo_ai_studio/millet-erp/backend/tests/Api.IntegrationTests/Identidad/BootstrapRolesMvpTests.cs) para verificar que el rol `admin-catalogos` contenga los 8 permisos esperados y que `admin-datos-maestros` contenga sus permisos granulares.

---

## 3. Matriz de Impacto en Frontend

| Tarjeta en `/admin` | Permiso Requerido | Estado Anterior para `admin-catalogos` | Estado Posterior |
|---|---|---|---|
| Monedas y tipos de cambio | `catalogos.monedas.gestionar` | ❌ Oculta |  Visible |
| Condiciones de pago | `catalogos.condiciones-pago.gestionar` | ❌ Oculta |  Visible |
| Incoterms | `catalogos.incoterms.gestionar` | ❌ Oculta |  Visible |
| Transportistas | `catalogos.transportistas.gestionar` | ❌ Oculta |  Visible |
| Unidades de medida | `catalogos.unidades-medida.gestionar` | ❌ Oculta |  Visible |
| Usos principales | `compartido.catalogos.administrar` |  Visible |  Visible |
| Categorías de artículo | `compartido.catalogos.administrar` |  Visible |  Visible |
| Formas de pago (SAT) | `compartido.catalogos.leer` |  Visible |  Visible |
| Impuestos | `compartido.catalogos.leer` |  Visible |  Visible |
| Usos CFDI (SAT) | `compartido.catalogos.leer` |  Visible |  Visible |
| Regímenes fiscales (SAT) | `compartido.catalogos.leer` |  Visible |  Visible |
