# Diseño — Módulo Administración (incluye Identidad, Catálogos y Datos Maestros)

> **Construido sobre:** [00-levantamiento-estado-actual.md](00-levantamiento-estado-actual.md) (Rev. 1, 2026-05-13).
>
> **Hereda decisiones de:** [ADR-0034](../../decisiones/0034-area-administracion-settings-hibrido.md) (modelo híbrido) y [ADR-0035](../../decisiones/0035-relocalizar-entidades-sharedkernel-a-modulos.md) (re-localización SharedKernel).
>
> **Estado:** propuesta de diseño v1 para revisión con el owner. Las decisiones marcadas como `[Asunción]` están listadas en §3.
>
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer este documento

- `[Decidido]` — fijado por ADR existente, mapa funcional cerrado, o sesión con el cliente.
- `[Asunción]` — propuesta razonable del diseñador, pendiente de confirmación. Listada en §3.
- `[Diferido]` — fuera de alcance v1, anotado para no perderlo.
- `[Pendiente]` — la decisión existe pero se cierra antes del 02-plan o antes de implementar el bloque correspondiente.

Este documento describe **qué construir y por qué**, no el código. La implementación seguirá las convenciones del repo (hexagonal, CQRS con MediatR, EF Core, FluentValidation, Mapster, Serilog, records, sealed por defecto, nullable reference types).

---

## 1. Posicionamiento y alcance

### 1.1 Ubicación en el ERP

- **Área:** Administración — transversal, primera área no-de-dominio del ERP.
- **Cuatro módulos lógicos** consolidados bajo esta área:
  1. **Administracion** — empresas, sucursales, departamentos, series, parámetros, auditoría.
  2. **Catalogos** — catálogos SAT y operativos (monedas, condiciones de pago, formas de pago, usos CFDI, regímenes fiscales, incoterms, transportistas, unidades de medida, tipos de cambio).
  3. **DatosMaestros** — proveedores, artículos.
  4. **Almacen** (MVP-light) — solo la entidad `Almacen` y su DbContext en este alcance (A1=b cerrada 2026-05-13). El alcance completo del módulo Almacén-no-prod (inventario, movimientos, recepciones) se aborda en su propio sprint cuando llegue.
- **Módulo existente reutilizado:** `Identidad` (ya implementado backend, falta UI). Para fines de UI, Identidad cae bajo `/admin/*`, pero su dominio permanece en `backend/src/Identidad/`.

> El término "módulo Admin" en este doc engloba los cinco: Administracion + Catalogos + DatosMaestros + Almacen + Identidad. Cuando se requiere precisión, se nombra el módulo concreto.

- **Schemas PostgreSQL** (post Fase B de ADR-0035, opcional/diferida):
  - `identidad` (existente).
  - `admin` (nuevo, para Administracion).
  - `catalogos` (nuevo, para Catalogos).
  - `datos_maestros` (nuevo, para DatosMaestros).
  - `almacen` (nuevo, para Almacen).
- **Schema físico Fase A (lo que se construye en MVP):** las tablas existentes permanecen en `compartido`; los DbContexts nuevos apuntan a `compartido` por `ToTable(..., schema: "compartido")`. Solo migraciones nuevas usan los schemas propios del módulo cuando se evalúe Fase B.
- **Proyectos .NET nuevos:** `backend/src/Administracion/`, `backend/src/Catalogos/`, `backend/src/DatosMaestros/`, `backend/src/Almacen/`. Cada uno con su propio dominio, aplicación, infraestructura y DbContext.

### 1.2 Alcance funcional v1 (MVP)

**Dentro:**

1. **Refactor ADR-0035 (F-Admin-PR0).** Re-localización de entidades de `SharedKernel/Domain/` a `Administracion`/`Catalogos`/`DatosMaestros`/`Almacen` (este último con A1=b). Sin cambios de comportamiento.
2. **Andamio + contrato SettingsSchema (F-Admin-PR1).** Registry `AdminSection`, landing `/admin`, engrane del topbar wireado, schemas creados (DbContexts), permisos canónicos `admin.*` + `identidad.*` (CRUD) + `catalogos.*` + `datos_maestros.*` + `almacen.*` mínimos. **Contrato `ISettingsSchemaProvider` + endpoints `GET /api/v1/<modulo>/settings/schema` y `PATCH /api/v1/<modulo>/settings/{clave}` cableados en SharedKernel** (A7=b). Compras adopta el contrato como exemplar.
3. **Empresas + Sucursales (F-Admin-PR2).** UI master-detail para gestión completa. Necesario primero — lo consume Compras OC multi-empresa.
4. **Roles + permisos (F-Admin-PR3).** UI sobre módulo Identidad. Matriz de permisos por módulo, asignación inline de permisos a rol. **Mapeo manual de grupos Entra ID → rol** (A3=a).
5. **Usuarios (F-Admin-PR4).** UI de usuarios + asignación rol×empresa.
6. **Datos Maestros (F-Admin-PR4.5).** UI de administración de Proveedores y Artículos bajo `/admin/datos-maestros/*` (los CRUD ya operados por Compras se reutilizan; UI agrega vistas y filtros admin).
7. **Catálogos SAT (F-Admin-PR5).** CRUD básico de Moneda + **tipos de cambio carga manual via UI inline** (A4=a), CondicionesPago, FormasPago, UsosCfdi, RegimenFiscal, Incoterm, Transportista, UnidadMedida.
8. **Series y folios (F-Admin-PR6).** CRUD de series por tipo de documento y empresa/sucursal con **ReinicioPeriodo: None (eterno) | Anual | Mensual** (A5=a ampliada).
9. **Auditoría + parámetros globales (F-Admin-PR7).** UI de bitácora consolidada + parámetros (TZ, formato fecha, redondeo).

**Fuera (cerrado en §10 de levantamiento o por ADR):**

- **Migración masiva desde SAP** — `[Diferido]` (consistente con Fase 7 de Compras según memoria `project_fase7_mvp_scope`).
- **Sync automatizado Entra ID → roles** — `[Diferido]` post-MVP. MVP usa mapeo manual por grupo (A3=a).
- **Bulk operations** (alta masiva de usuarios) — `[Diferido]` v1.1.
- **Portal de auto-servicio** (cambio de contraseña, perfil) — `[Diferido]`. Hoy lo gestiona Entra ID.
- **Sync de tipos de cambio (DOF/Banxico)** — `[Diferido]`. MVP es carga manual.
- **UI de feature flags** — `[Diferido]`. Settings de módulo son por empresa via tablas, no flags.
- **Re-localización física de schemas (Fase B de ADR-0035)** — `[Diferido]`, sin fecha.
- **Re-mover `Almacen` a módulo Almacén-no-prod** — `[Diferido]` hasta que ese módulo exista.
- **Schema rename `compartido` → `admin`/`catalogos`/`datos_maestros`** — `[Diferido]`. Fase A de ADR-0035 mantiene `compartido`.

### 1.3 Bounded contexts y agregados

| Contexto | Agregado raíz | Entidades hijas | Schema | Módulo .NET |
|---|---|---|---|---|
| Identidad | `Usuario` | `UsuarioEmpresaRol`, `UsuarioPreferencia` | `identidad` | `Identidad` (existente) |
| Identidad | `Rol` | `RolPermiso`, `RestriccionRol` | `identidad` | `Identidad` |
| Identidad | `Permiso` (catálogo) | — | `identidad` | `Identidad` |
| Administracion | `Empresa` | `Sucursal` (cuando Sucursal pertenece a Empresa multi-tenant) | `compartido` (Fase A) | `Administracion` |
| Administracion | `Departamento` | — | `compartido` | `Administracion` |
| Almacen | `Almacen` | — | `compartido` (Fase A) → `almacen` (Fase B) | `Almacen` (módulo MVP-light, A1=b) |
| Administracion | `Serie` | `Folio`, `SecuenciaFolio` | `compartido` (Fase A) | `Administracion` |
| Administracion | `ParametroGlobal` | — | `compartido` | `Administracion` |
| Administracion | `RegistroBitacora` (read model) | — | `compartido` | `Administracion` |
| Catalogos | `Moneda` | `TipoCambio` | `compartido` | `Catalogos` |
| Catalogos | `CondicionesPago` | — | `compartido` | `Catalogos` |
| Catalogos | `FormaPago` (SAT) | — | `compartido` | `Catalogos` |
| Catalogos | `UsoCfdi` (SAT) | — | `compartido` | `Catalogos` |
| Catalogos | `RegimenFiscal` (SAT) | — | `compartido` | `Catalogos` |
| Catalogos | `Incoterm` | — | `compartido` | `Catalogos` |
| Catalogos | `Transportista` | — | `compartido` | `Catalogos` |
| Catalogos | `UnidadMedida` | — | `compartido` | `Catalogos` |
| DatosMaestros | `Proveedor` | — | `compartido` | `DatosMaestros` |
| DatosMaestros | `Articulo` | — | `compartido` | `DatosMaestros` |

> `Sucursal` puede modelarse como entidad hija de `Empresa` o como agregado independiente con FK a Empresa. **[Decidido en 02-plan]**: agregado independiente con FK, alineado con la implementación actual.

## 2. Criterio "qué va a Admin transversal vs al módulo"

Regla para resolver dudas durante implementación:

| Pregunta | Si la respuesta es… | Entonces vive en… |
|---|---|---|
| ¿Lo consumen 2+ módulos del back-office? | Sí | `Administracion` o `Catalogos` |
| ¿Es un catálogo SAT/fiscal/operativo cross-módulo? | Sí | `Catalogos` |
| ¿Es identidad/RBAC/usuario/rol? | Sí | `Identidad` (UI en `/admin`) |
| ¿Es estructura organizacional multi-tenant? | Sí | `Administracion` |
| ¿Es entidad de negocio con ciclo de vida propio compartida (Proveedor, Artículo)? | Sí | `DatosMaestros` |
| ¿Es un setting que solo aplica a UN módulo de negocio? | Sí | El módulo de negocio (patrón ADR-0033) |
| ¿Es una regla específica de Compras/Facturación/CxC/etc.? | Sí | El módulo correspondiente |

### Ejemplos de aplicación

- "Umbrales de autorización de OC" → `Compras` (regla específica).
- "Tipos de cambio MXN/USD" → `Catalogos` (cross-módulo).
- "Plantillas de CFDI por cliente" → `Facturacion` (específico).
- "Empresas y sucursales" → `Administracion` (transversal, multi-tenant).
- "Política de antigüedad de saldos" → `CuentasPorCobrar` (específico).
- "Roles del sistema" → `Identidad` (transversal).
- "Catálogo de usos CFDI" → `Catalogos` (SAT).
- "Bitácora de cambios al setting de Compras" → la captura `Compras.ComprasSettings` vía auditoría; la **UI consolidada de auditoría** vive en `Administracion/auditoria`.

## 3. Decisiones cerradas

Asunciones A1–A7 del [00-levantamiento §10](00-levantamiento-estado-actual.md#10-decisiones-cerradas) cerradas por el owner el **2026-05-13**. Resumen aplicado al diseño:

- **[A1 = b · Decidido]** `Almacen` vive en módulo `Almacen` (MVP-light) — no en `Administracion`. Schema (Fase B) `almacen`. Proyecto `backend/src/Almacen/`.
- **[A2 · Decidido]** Set de roles seedeados confirmado (7 roles base, ver §6.4).
- **[A3 = a · Decidido]** Mapeo Entra ID → rol **manual por grupo** vía UI `/admin/roles/$id`.
- **[A4 = a · Decidido]** Tipos de cambio carga **manual** UI inline. DOF/Banxico sync diferido (`<TipoCambioSync>` en §12).
- **[A5 = a · Decidido (ampliada)]** Series soportan **anual, mensual o eterno** (sin reinicio). `ReinicioPeriodo: None | Anual | Mensual`.
- **[A6 = a · Decidido]** `Departamento` queda en `Administracion`.
- **[A7 = b · Decidido]** Auto-renderizado de settings genéricos **desde día 1**. Contrato `SettingsSchema` en §6.5. UI custom soportada vía `Mostrar = Custom`.

## 4. Modelo de dominio (alto nivel)

> Detalle de propiedades, invariantes y métodos públicos se elabora en el código durante PR2–PR7. Aquí se documenta solo la forma estructural.

### 4.1 Identidad (existente — sin cambios estructurales)

```
Usuario { Id, Email, NombreCompleto, EntraIdObjectId?, DepartamentoId?, Activo, ... }
  └─ UsuarioEmpresaRol { UsuarioId, EmpresaId, RolId, FechaAsignacion }
  └─ UsuarioPreferencia { UsuarioId, Clave, Valor }

Rol { Id, Nombre, Descripcion, EsSystem, ... }
  └─ RolPermiso { RolId, PermisoCanonico }
  └─ RestriccionRol { RolId, TipoRestriccion, Valor }

Permiso { Canonico, Descripcion, Modulo }  // seedeado desde PermisosCanonicos.cs
```

### 4.2 Administracion (nuevo módulo)

```
Empresa { Id, Rfc, RazonSocial, NombreComercial?, RegimenFiscalId, Activa, ... }
  ← Sucursal (FK)
  ← Departamento (FK)

Sucursal { Id, EmpresaId, Clave, Nombre, Direccion?, Estatus, ... }
Departamento { Id, EmpresaId, Clave, Nombre, Estatus, ... }

Serie { Id, EmpresaId, SucursalId?, TipoDocumento, Prefijo, Sufijo?,
        ReinicioPeriodo (None|Anual|Mensual), Activa }
  // ReinicioPeriodo: None = eterno (sin reinicio, correlativo eterno).
  //                  Anual = reinicia el 1 de enero a 0001.
  //                  Mensual = reinicia el día 1 de cada mes a 0001.
  // (A5=a ampliada — decidido 2026-05-13.)
  └─ SecuenciaFolio { SerieId, PeriodoClave, UltimoNumero }
  // PeriodoClave: "" para None, "2026" para Anual, "2026-05" para Mensual.
  // Folio se genera al consumir desde un módulo de negocio (transaccional).

ParametroGlobal { Id, Clave, Valor, Tipo, Modulo?, Descripcion, ... }
// p.ej. (Clave="TimezoneDefault", Valor="America/Mexico_City", Tipo="string", Modulo=null)

RegistroBitacora (read model) { Id, FechaUtc, UsuarioId, Modulo, Recurso, Accion, Detalle (jsonb), ... }
// Se materializa desde AuditLogEntry (ADR-0008)
```

### 4.2.1 Almacen (módulo MVP-light)

```
Almacen { Id, SucursalId, Clave, Nombre, Estatus, ... }
// MVP-light: solo la entidad, su DbContext (AlmacenDbContext) y CRUD básico.
// El alcance completo del módulo Almacén-no-prod (inventario, movimientos,
// recepciones cross-Compras) se aborda en su propio sprint cuando llegue.
// Decisión A1=b cerrada 2026-05-13.
```

### 4.3 Catalogos

```
Moneda { Id, Codigo (MXN, USD, EUR), Nombre, Decimales, Activa }
  └─ TipoCambio { MonedaId, Fecha, ValorEnMxn, Origen (Manual|DOF|Banxico) }

CondicionesPago { Id, Codigo, Nombre, DiasCredito, Activa }
FormaPago { Id, ClaveSat, Nombre, Activa }      // SAT
UsoCfdi { Id, ClaveSat, Nombre, AplicaA, Activo } // SAT
RegimenFiscal { Id, ClaveSat, Nombre, AplicaTipoPersona, Activo } // SAT
Incoterm { Id, Codigo, Descripcion, Activa }
Transportista { Id, Rfc?, Nombre, Activo, ... }
UnidadMedida { Id, ClaveSat, Codigo, Nombre, Activa }  // SAT + libre
```

### 4.4 DatosMaestros (ya existentes, post-refactor a su módulo)

```
Proveedor { Id, Rfc, RazonSocial, NombreComercial?, TipoPersona, RegimenFiscalId?, Activo, ... }
Articulo { Id, Codigo, Descripcion, UnidadMedidaId, Naturaleza, Activo, ... }
```

### 4.5 Invariantes destacados

- `Empresa.Rfc` único.
- `Sucursal.Clave` único por empresa.
- `Departamento.Clave` único por empresa.
- `Moneda.Codigo` único (ISO 4217).
- `Permiso.Canonico` único.
- `Rol.Nombre` único.
- `Serie` única por (EmpresaId, SucursalId?, TipoDocumento, Prefijo).
- `Usuario.Email` único.
- `Usuario.EntraIdObjectId` único cuando no es null.
- Un super-admin no puede eliminarse a sí mismo si es el único.
- No se puede eliminar un rol asignado a usuarios; se desactiva.
- No se puede eliminar un catálogo SAT referenciado por entidades operativas; se desactiva.

## 5. Casos de uso (commands/queries principales)

Listado a alto nivel. Cada uno se implementa como command/query MediatR con su handler, validator FluentValidation, y endpoint REST.

### Identidad
- `CrearUsuarioCommand`, `ActualizarUsuarioCommand`, `DesactivarUsuarioCommand`, `AsignarRolAUsuarioCommand`, `RevocarRolDeUsuarioCommand`.
- `CrearRolCommand`, `ActualizarRolCommand`, `EliminarRolCommand` (si no tiene asignaciones), `AsignarPermisosARolCommand`.
- `ListarUsuariosQuery`, `ObtenerUsuarioQuery`, `ListarRolesQuery`, `ListarPermisosQuery`.

### Administracion
- `CrearEmpresaCommand`, `ActualizarEmpresaCommand`, `DesactivarEmpresaCommand`.
- `CrearSucursalCommand`, `ActualizarSucursalCommand`, `DesactivarSucursalCommand`.
- `CrearDepartamentoCommand`, `ActualizarDepartamentoCommand`.
- `CrearSerieCommand`, `ActualizarSerieCommand`, `DesactivarSerieCommand`.
- `ActualizarParametroGlobalCommand`.
- `ListarEmpresasQuery`, `ListarSucursalesQuery`, `ListarSeriesQuery`, `ConsultarBitacoraQuery`.

### Catalogos
- `CrearMonedaCommand`, `ActualizarMonedaCommand`, `RegistrarTipoCambioCommand`.
- `CrearCondicionesPagoCommand`, `ActualizarCondicionesPagoCommand`.
- `CrearIncotermCommand`, `CrearTransportistaCommand`, `CrearUnidadMedidaCommand` (+ Actualizar y Desactivar para cada uno).
- Seeds SAT son inmutables vía UI; se actualizan vía migración (ver §8).
- `ListarMonedasQuery`, `ListarTiposCambioPorMonedaQuery`, etc.

### DatosMaestros
- `CrearProveedorCommand` (ya existe en `SharedKernel/Application/Catalogos/` — se mueve).
- `CrearArticuloCommand` (ya existe — se mueve).
- `ListarProveedoresQuery`, `ListarArticulosQuery`.

### Consumo de Series (en módulos de negocio)
- `ReservarFolioCommand` (en `Administracion.Application`) — recibe `(EmpresaId, SucursalId?, TipoDocumento, Periodo)`, retorna folio reservado. Idempotente vía `Idempotency-Key`. Lo invocan handlers de Compras (`CrearOrdenCompraVaciaHandler` ya tiene su propio `FolioSecuencia`, se reconciliará en F-Admin-PR6).

## 6. Frontera y contratos

### 6.1 Registry `AdminSection` (contrato de extensibilidad)

```typescript
// frontend/src/lib/admin/registry.ts
export interface AdminSection {
  id: string;
  modulo: 'admin' | 'identidad' | 'catalogos' | 'datos_maestros' | 'almacen'
        | 'compras' | 'facturacion' | 'cxc' | 'cxp'
        | 'activos' | 'contabilidad' | 'reportes' | 'aw';
  titulo: string;
  descripcion: string;
  icon: LucideIcon;
  href: string;
  permisoRequerido: string;
  orden: number;
  grupo: 'identidad' | 'organizacion' | 'catalogos' | 'datos_maestros' | 'modulos';
  // Solo aplica para grupo='modulos'. Si 'auto', el card linkea a
  // /admin/<modulo>/settings con form auto-renderizado desde el schema.
  // Si 'custom', linkea a 'href' directamente (UI dedicada del módulo).
  displayMode?: 'auto' | 'custom';
}

export const adminRegistry: AdminSection[] = [
  ...identidadAdminCards,        // Usuarios, Roles, Permisos
  ...administracionAdminCards,   // Empresas, Departamentos, Series, Parámetros, Auditoría
  ...catalogosAdminCards,        // Monedas, CondicionesPago, etc.
  ...datosMaestrosAdminCards,    // Proveedores, Artículos
  ...almacenAdminCards,          // Almacenes (MVP-light)
  ...comprasAdminCards,          // /compras/configuracion (custom)
  // ...facturacionAdminCards, etc. — cada módulo nuevo agrega aquí
];
```

Cada módulo expone su array `<modulo>AdminCards: AdminSection[]` en `frontend/src/modules/<modulo>/admin.ts`.

### 6.2 Engrane del topbar — wireup

[`Topbar.tsx:182`](../../../frontend/src/components/layout/Topbar.tsx#L182):

```tsx
// Antes:
<Button variant="ghost" size="icon" disabled aria-label="Configuración">
  <Settings className="h-4 w-4" />
</Button>

// Después (F-Admin-PR1):
{usuarioPuedeVerAdmin && (
  <Button variant="ghost" size="icon" asChild aria-label="Configuración">
    <Link to="/admin"><Settings className="h-4 w-4" /></Link>
  </Button>
)}
```

`usuarioPuedeVerAdmin` retorna `true` si el usuario tiene **cualquier** permiso que aparezca en el registry filtrado.

### 6.3 Permisos canónicos

Convención: `<modulo>.<recurso>.<accion>`.

```csharp
// PermisosCanonicos.cs — agregar:
public const string AdminEmpresasLeer = "admin.empresas.leer";
public const string AdminEmpresasCrear = "admin.empresas.crear";
public const string AdminEmpresasEditar = "admin.empresas.editar";
public const string AdminSucursalesGestionar = "admin.empresas.sucursales.gestionar";
public const string AdminDepartamentosGestionar = "admin.departamentos.gestionar";
public const string AdminSeriesGestionar = "admin.series.gestionar";
public const string AdminParametrosEditar = "admin.parametros.editar";
public const string AdminAuditoriaLeer = "admin.auditoria.leer";

public const string IdentidadUsuariosLeer = "identidad.usuarios.leer";
public const string IdentidadUsuariosCrear = "identidad.usuarios.crear";
public const string IdentidadUsuariosEditar = "identidad.usuarios.editar";
public const string IdentidadUsuariosDesactivar = "identidad.usuarios.desactivar";
public const string IdentidadRolesLeer = "identidad.roles.leer";
public const string IdentidadRolesCrear = "identidad.roles.crear";
public const string IdentidadRolesEditar = "identidad.roles.editar";
public const string IdentidadRolesEliminar = "identidad.roles.eliminar";
public const string IdentidadRolesAsignarPermisos = "identidad.roles.asignar_permisos";
public const string IdentidadPermisosLeer = "identidad.permisos.leer";

public const string CatalogosMonedasGestionar = "catalogos.monedas.gestionar";
public const string CatalogosTiposCambioGestionar = "catalogos.tipos_cambio.gestionar";
public const string CatalogosCondicionesPagoGestionar = "catalogos.condiciones_pago.gestionar";
public const string CatalogosFormasPagoGestionar = "catalogos.formas_pago.gestionar";
public const string CatalogosUsosCfdiGestionar = "catalogos.usos_cfdi.gestionar";
public const string CatalogosRegimenesFiscalesGestionar = "catalogos.regimenes_fiscales.gestionar";
public const string CatalogosIncotermsGestionar = "catalogos.incoterms.gestionar";
public const string CatalogosTransportistasGestionar = "catalogos.transportistas.gestionar";
public const string CatalogosUnidadesMedidaGestionar = "catalogos.unidades_medida.gestionar";

public const string DatosMaestrosProveedoresGestionar = "datos_maestros.proveedores.gestionar";
public const string DatosMaestrosArticulosGestionar = "datos_maestros.articulos.gestionar";
```

> Cada permiso ya existe o se agrega en su PR correspondiente. F-Admin-PR1 agrega los del andamio (`admin.empresas.leer`, `identidad.usuarios.leer`, `identidad.roles.leer` mínimos). Posteriores van con cada feature.

### 6.4 Roles seedeados del MVP

Set decidido por owner (A2 cerrada 2026-05-13):

| Rol | Permisos clave |
|---|---|
| **Super-administrador** | Todos los permisos `admin.*`, `identidad.*`, `catalogos.*`, `datos_maestros.*`, `almacen.*` y `<modulo>.settings.*`. |
| **Administrador de identidad** | `identidad.*` (CRUD). |
| **Administrador organizacional** | `admin.empresas.*`, `admin.departamentos.*`. |
| **Administrador de catálogos** | `catalogos.*`. |
| **Administrador de datos maestros** | `datos_maestros.*`. |
| **Auditor** | `admin.auditoria.leer`, `*.leer` (read-only sobre todo el área Admin). |
| **Administrador Compras** | `compras.configuracion.leer` + `compras.configuracion.editar` (ya existentes vía ADR-0033). |

Seeds en `Identidad/Infrastructure/Migrations/` extendiendo `BootstrapSuperAdminHostedService.cs`.

### 6.5 Contrato `SettingsSchema` (A7=b)

Cada módulo de negocio implementa `ISettingsSchemaProvider` y publica dos endpoints:

```
GET  /api/v1/<modulo>/settings/schema
  → 200 { items: SettingItem[] }
  // Filtrado por permisos del usuario (oculta items que el usuario no puede leer).

PATCH /api/v1/<modulo>/settings/{clave}
  → headers: Idempotency-Key (ADR-0020)
  → body: { valor: any }
  → 200 { valor: any }
  → 422 si valida contra Validacion.* falla
  → 403 si no tiene PermisoEditar
```

Modelo `SettingItem` (en `SharedKernel.Application.Settings`):

```csharp
public sealed record SettingItem(
    string Clave,                       // "AutoGenerarOcAlAutorizar"
    string Etiqueta,                    // "Auto-generar OC al autorizar"
    string Descripcion,                 // texto largo para tooltip/help
    TipoSetting Tipo,                   // Bool | Int | Decimal | String | Enum | Fecha
    object? Default,
    object? Valor,                      // actual para la empresa activa
    ValidacionSetting? Validacion,      // { min?, max?, pattern?, opciones? (enum) }
    string PermisoLeer,
    string PermisoEditar,
    DisplayMode Mostrar,                // Auto | Custom
    string? RutaCustom,                 // si Custom, link a UI dedicada
    string? AlertaCambio                // texto en modal de confirmación si non-trivial
);

public enum TipoSetting { Bool, Int, Decimal, String, Enum, Fecha }
public enum DisplayMode { Auto, Custom }
public sealed record ValidacionSetting(
    decimal? Min = null,
    decimal? Max = null,
    string? Pattern = null,
    string[]? Opciones = null);
```

**Implementación**:

- `SharedKernel.Application.Settings.ISettingsSchemaProvider` se define como contrato. Endpoint genérico `MapGroup("/api/v1/{modulo}/settings")` registrado en `Api.Endpoints.Settings.SettingsEndpoints`.
- El endpoint resuelve el provider por `{modulo}` vía `IEnumerable<ISettingsSchemaProvider>` + filtrado por `Provider.Modulo`. 404 si no se encuentra; 403 si el caller no tiene `PermisoLeer` para ningún ítem.
- Cada módulo registra su provider:
  ```csharp
  services.AddScoped<ISettingsSchemaProvider, ComprasSettingsSchemaProvider>();
  ```

**Frontend**: el landing `/admin` filtra cards `displayMode = 'auto'` del registry y muestra link a `/admin/<modulo>/settings`; la página renderiza un form a partir del schema con shadcn/ui (`Switch` para Bool, `Input` numérico para Int/Decimal, `Select` para Enum, `DatePicker` para Fecha). Las `'custom'` linkean a `RutaCustom` del módulo (ej. `ComprasSettings`).

**Compras como exemplar** (ADR-0033): el setting `AutoGenerarOcAlAutorizar` se marca `Mostrar = Custom, RutaCustom = "/compras/configuracion"` porque tiene efectos cross-cutting en el flujo RQ→OC. Otros settings de Compras que sean booleanos simples pueden marcarse `Mostrar = Auto`.

**Auditoría**: cada PATCH al setting genera un `AuditLogEntry` vía interceptor (ADR-0008). La UI de `/admin/auditoria` filtra por módulo y muestra antes/después.

### 6.6 API REST

URLs estables siguiendo ADR-0021. Ejemplos:

```
GET    /api/v1/identidad/usuarios
POST   /api/v1/identidad/usuarios
PATCH  /api/v1/identidad/usuarios/{id}
DELETE /api/v1/identidad/usuarios/{id}   (soft-delete)
POST   /api/v1/identidad/usuarios/{id}/roles
DELETE /api/v1/identidad/usuarios/{userId}/roles/{rolId}

GET    /api/v1/identidad/roles
POST   /api/v1/identidad/roles
PATCH  /api/v1/identidad/roles/{id}
POST   /api/v1/identidad/roles/{id}/permisos

GET    /api/v1/admin/empresas
POST   /api/v1/admin/empresas
GET    /api/v1/admin/empresas/{id}
PATCH  /api/v1/admin/empresas/{id}
POST   /api/v1/admin/empresas/{empresaId}/sucursales
POST   /api/v1/admin/empresas/{empresaId}/departamentos

GET    /api/v1/admin/series
POST   /api/v1/admin/series
POST   /api/v1/admin/series/{id}/reservar   // commando para módulos

GET    /api/v1/admin/auditoria?modulo=&recurso=&desde=&hasta=
GET    /api/v1/admin/parametros
PATCH  /api/v1/admin/parametros/{clave}

GET    /api/v1/catalogos/monedas
POST   /api/v1/catalogos/monedas
POST   /api/v1/catalogos/monedas/{id}/tipos-cambio
GET    /api/v1/catalogos/condiciones-pago
GET    /api/v1/catalogos/formas-pago        // SAT, read-mostly
GET    /api/v1/catalogos/usos-cfdi          // SAT, read-mostly
GET    /api/v1/catalogos/regimenes-fiscales // SAT, read-mostly
GET    /api/v1/catalogos/incoterms
GET    /api/v1/catalogos/transportistas
GET    /api/v1/catalogos/unidades-medida

GET    /api/v1/datos-maestros/proveedores
POST   /api/v1/datos-maestros/proveedores
GET    /api/v1/datos-maestros/articulos
POST   /api/v1/datos-maestros/articulos
```

> URLs existentes (`/api/v1/organizacion/empresas`, `/api/v1/catalogos/*`) se mantienen estables. PR0 (refactor ADR-0035) no cambia URLs; solo reorganiza código. Nuevos endpoints van directamente bajo `/admin/*`, `/datos-maestros/*`, `/almacen/*`.

Adicionalmente, los endpoints de SettingsSchema genéricos (§6.5):

```
GET    /api/v1/{modulo}/settings/schema
PATCH  /api/v1/{modulo}/settings/{clave}
```

### 6.7 Eventos de integración

Mínimos en MVP. Patrón Outbox (ADR-0009). Eventos a publicar desde Administracion:

- `EmpresaCreadaEvent` — para que módulos creen estructuras dependientes (settings por empresa).
- `SucursalCreadaEvent` — idem.
- `RolPermisosActualizadosEvent` — para invalidar caches de autorización.
- `UsuarioRolAsignadoEvent` / `UsuarioRolRevocadoEvent` — idem.

Consumidos por módulos de negocio (ej. Compras crea `ComprasSettings` por defecto al recibir `EmpresaCreadaEvent`).

## 7. Frontend — rutas y patrones

### 7.1 Árbol de rutas

```
/admin                                       — landing (grid de cards agrupadas)
/admin/usuarios                              — P3 master-detail
/admin/usuarios/$id
/admin/roles                                 — P3 master-detail
/admin/roles/$id                             — tabs: datos | permisos (inline) | usuarios asignados
/admin/permisos                              — P1 read-only (catálogo)

/admin/empresas                              — P3
/admin/empresas/$id                          — tabs: datos | sucursales | departamentos | series | settings
/admin/departamentos                         — P1 + Sheet
/admin/series                                — P1 + Sheet

/admin/catalogos/monedas                     — P3 + inline tipos de cambio
/admin/catalogos/condiciones-pago            — P1 + Sheet
/admin/catalogos/formas-pago                 — P1 read-only (SAT)
/admin/catalogos/usos-cfdi                   — P1 read-only (SAT)
/admin/catalogos/regimenes-fiscales          — P1 read-only (SAT)
/admin/catalogos/incoterms                   — P1 + Sheet
/admin/catalogos/transportistas              — P1 + Sheet
/admin/catalogos/unidades-medida             — P1 + Sheet

/admin/datos-maestros/proveedores            — P3
/admin/datos-maestros/proveedores/$id
/admin/datos-maestros/articulos              — P3
/admin/datos-maestros/articulos/$id

/admin/parametros                            — formulario simple
/admin/auditoria                             — P2 (filtros server-side)
```

### 7.2 Landing `/admin`

Reusa `AppLauncherModal` + `AppLauncherCard` (ADR-0032). El componente recibe `adminRegistry` filtrado por permisos del usuario y agrupa por `grupo`:

```
┌─ Identidad y acceso ────────────────────────┐
│  [Usuarios]  [Roles y permisos]             │
├─ Organización ──────────────────────────────┤
│  [Empresas]  [Departamentos]  [Series]      │
├─ Catálogos globales ────────────────────────┤
│  [Monedas]  [Condiciones de pago]  [SAT...] │
├─ Datos maestros ────────────────────────────┤
│  [Proveedores]  [Artículos]                 │
├─ Configuración por módulo ──────────────────┤
│  [Compras]  ...                             │
└─────────────────────────────────────────────┘
```

### 7.3 Visibilidad del engrane

El icono Settings en el topbar se muestra si el usuario tiene **al menos un** permiso que aparece en `adminRegistry` (filtro precomputado en el hook `useAdminAccess()`). Sin permisos relevantes, se oculta.

### 7.4 Patrones aplicados

| Pantalla | Patrón |
|---|---|
| `/admin/*` listados con detalle | **P3** master-detail |
| `/admin/catalogos/*` simples | **P1** bandeja |
| `/admin/auditoria` | **P2** bandeja filtrada server-side |
| "Nuevo usuario/rol/empresa/moneda" | **P4** Sheet |
| Items dentro de master (permisos en rol, sucursales en empresa, tipos de cambio en moneda) | **Inline forms** (memoria `feedback_inline_no_modal_para_items`) — **nunca modal** |

## 8. Seeds y carga inicial

| Catálogo | Origen | Mantenimiento |
|---|---|---|
| Permisos canónicos | `Identidad/Domain/PermisosCanonicos.cs` | Migración seedea al cambiar. Cada PR agrega los suyos. |
| Roles base | `BootstrapSuperAdminHostedService.cs` extendido | Idempotente. Crea 7 roles MVP si no existen. |
| Empresa MVP | Migración seed con la(s) empresa(s) del cliente | Manual al on-boarding. |
| Sucursales/Departamentos MVP | Migración seed | Manual al on-boarding. |
| Monedas (MXN, USD, EUR base) | Seed inicial | UI permite agregar más. |
| FormasPago SAT | Seed completo desde catálogo SAT | Migración aditiva si SAT publica. |
| UsosCfdi SAT | Seed completo | Idem. |
| RegimenesFiscales SAT | Seed completo (ya existe parcial vía `CatalogosOcSeed`) | Idem. |
| UnidadesMedida SAT | Seed completo | Idem. |
| Incoterms | Seed completo (ya existe vía `CatalogosOcSeed`) | Sin actualizaciones esperadas. |
| Series MVP | Manual via UI al on-boarding | — |
| Tipos de cambio | Manual via UI | Carga diaria/según necesidad. |

## 9. Auditoría

Modelo `AuditLogEntry` (ADR-0008) ya existe. Admin agrega:

- Interceptor activo en todos los DbContexts del módulo (`AdministracionDbContext`, `CatalogosDbContext`, `DatosMaestrosDbContext`). Ya configurado en `BaseDbContext`.
- Query `ConsultarBitacoraQuery` con filtros: módulo, recurso, acción, usuario, rango de fechas.
- UI `/admin/auditoria` (P2 bandeja filtrada server-side).

Detalle en el campo `Detalle (jsonb)`: antes/después del cambio para entidades marcadas `IAuditable`. Se omite payload sensible (passwords, secrets) — la auditoría ya filtra estos campos en el interceptor.

## 10. Multi-tenant (ADR-0011)

- `Empresa` es entidad raíz multi-tenant.
- `UsuarioEmpresaRol` ya permite que un usuario tenga roles distintos en empresas distintas.
- `EmpresaSelector` (existente en topbar) mantiene el `EmpresaId` activo en el contexto.
- Endpoints admin respetan el contexto: `GET /api/v1/admin/sucursales` retorna solo sucursales de la empresa activa. Super-admin puede listar transversal con filtro.
- Settings por empresa: `ComprasSettings` (ADR-0033) ya tiene `EmpresaId`. Cada módulo nuevo replica.

## 11. Tests

Estrategia ADR-0016 (xUnit + Testcontainers + Playwright). Mínimos por PR:

- **F-Admin-PR0** (refactor): suite existente debe pasar 100% sin cambios funcionales. PR no merge-able si rompe tests.
- **F-Admin-PR1** (andamio): test que verifica que el engrane aparece solo con permisos relevantes; test que landing renderiza secciones según permisos.
- **F-Admin-PR2** (Empresas): tests de invariantes (`Rfc` único, no eliminar empresa con sucursales activas).
- **F-Admin-PR3** (Roles): tests de invariantes (no eliminar rol asignado).
- **F-Admin-PR4** (Usuarios): tests de mapeo Entra ID, asignación rol×empresa.
- **F-Admin-PR5** (Catálogos): tests de seeds idempotentes.
- **F-Admin-PR6** (Series): tests de generación de folio thread-safe + idempotencia.
- **F-Admin-PR7** (Auditoría/Parámetros): tests de query con filtros.

## 12. Dependencias de plataforma pendientes

Sección obligatoria por [ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md).

| Pieza | Ticket / Identificador | NoOp en uso hoy | Cómo se wirea |
|---|---|---|---|
| Bitácora UI consolidada (`/admin/auditoria`) | `<AuditUI>` | El modelo `AuditLogEntry` y el interceptor están activos. Hoy no hay endpoint de listado consolidado. | F-Admin-PR7 agrega `GET /api/v1/admin/auditoria` + UI. |
| Mapeo Entra ID → Roles automático | `<EntraIdMapping>` | Mapeo manual via `UsuarioEmpresaRol`. | Post-MVP: hosted service que sincroniza grupos Entra ID → roles definidos. |
| Sync tipos de cambio (DOF/Banxico) | `<TipoCambioSync>` | Carga manual via UI. | Post-MVP: hosted service NCrontab (ADR-0022) consume DOF/Banxico. |
| Re-localización física de schemas (Fase B ADR-0035) | `<SchemaRename>` | Schemas físicos siguen siendo `compartido`. Código organizado en 3 módulos. | Cuando exista razón concreta (BI externo, política de seguridad). |
| Re-mover `Almacen` a módulo Almacén-no-prod | `<AlmacenRemove>` | `Almacen` vive provisionalmente en `Administracion`. | Cuando se construya el módulo Almacén-no-prod. |
| Migración masiva de catálogos desde SAP | `<CatalogosSapImport>` | Seeds versionados cargan catálogos SAT y operativos básicos. Datos legacy del cliente se cargan vía seeds + UI. | Si el cliente requiere migración masiva post-MVP. |
| Auto-renderizado de settings genéricos | `<SettingsAutoRender>` | Cada módulo provee UI custom. | Evaluable cuando haya 4+ módulos con settings simples. |

Cualquier stub adicional introducido durante implementación se agrega aquí con `PLATFORM-TODO(<identificador>):` en código (memoria `feedback_platform_debt_tracking`).

## 13. Rev.

- **2026-05-13** — Rev. 2. Decisiones A1–A7 cerradas (ver §3). Cambios estructurales: §1.1 agrega módulo `Almacen` (A1=b); §1.2 reordena fases y agrega contrato SettingsSchema en F-Admin-PR1 (A7=b); §4.2 separa `Almacen` a §4.2.1; §6.1 incluye `displayMode` en registry; nueva §6.5 con contrato `SettingsSchema` completo; §6.6 (antes §6.5) y §6.7 (antes §6.6) renumeradas. Autor: Claude.
- **2026-05-13** — Rev. 1. Diseño inicial v1. Autor: Claude. Pendiente validación owner. Incluye §12 según ADR-0031.
