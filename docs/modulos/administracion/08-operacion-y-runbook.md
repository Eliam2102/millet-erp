# Operación y Runbook — Módulo Administración

> **Construido sobre:** todos los docs anteriores del módulo (00–07) y los ADRs aplicables (-0003, -0007, -0008, -0011, -0028, -0030, -0031, -0033, -0034, -0035).
>
> **Audiencia:** ingeniería de operaciones, on-call, owner. Contiene procedimientos operativos del módulo Administración: bootstrap, seeds, recuperación, rotación, troubleshooting.
>
> **Estado:** propuesta para revisión.
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer

Cada procedimiento sigue la estructura:

- **Cuándo se ejecuta** — el trigger (deploy nuevo ambiente, recuperación, mantenimiento programado, etc.).
- **Quién lo ejecuta** — rol responsable.
- **Pre-requisitos** — qué debe estar listo antes.
- **Pasos** — comandos / clicks concretos, en orden.
- **Verificación** — cómo confirmar que el procedimiento tuvo éxito.
- **Si algo falla** — error esperable + remediación.

> Los procedimientos están escritos asumiendo un solo ambiente activo en MVP (`dev`, ADR-0028). Cuando entren `qa-mini` y `prod`, los procedimientos aplican con los ajustes obvios (nombres de recursos, credenciales).

---

## 1. Bootstrap inicial del ambiente

### 1.1 Bootstrap del primer super-administrador

**Cuándo:** la primera vez que se levanta un ambiente (`dev`, `qa-mini`, `prod`) o tras una recuperación que dejó la BD vacía.

**Quién:** ingeniería de plataforma con acceso a Key Vault y variables de entorno.

**Pre-requisitos:**

- Ambiente desplegado vía Bicep (ADR-0028).
- BD PostgreSQL accesible.
- Migraciones de `Identidad` aplicadas (al menos las que crean `identidad.usuarios`, `identidad.roles`, `identidad.permisos`).
- Secretos en Key Vault: `BootstrapSuperAdminEmail`, `BootstrapSuperAdminEntraIdObjectId` (este último opcional en dev, obligatorio en qa/prod).

**Pasos:**

1. Verificar que `BootstrapSuperAdminHostedService` esté registrado en `Program.cs`. La pieza ya existe en el repo desde antes de Admin (ver memoria de auditoría 2.2 del 00-levantamiento).

2. Configurar los settings vía Key Vault:
   ```bash
   az keyvault secret set --vault-name kv-millet-dev-mxc-01 --name BootstrapSuperAdminEmail --value "eduardo.paredes@tiglass.net"
   az keyvault secret set --vault-name kv-millet-dev-mxc-01 --name BootstrapSuperAdminEntraIdObjectId --value "<object-id-de-eduardo-en-entra-id>"
   ```

   En `dev`, el `EntraIdObjectId` puede omitirse — `FakeForLocalDev` (ADR-0015) lo resuelve.

3. Iniciar la app. Al startup, `BootstrapSuperAdminHostedService` ejecuta:
   - Si el rol `Super-administrador` no existe → lo crea (idempotente).
   - Si el usuario con `BootstrapSuperAdminEmail` no existe → lo crea.
   - Si la asignación `UsuarioEmpresaRol` (Usuario × Empresa × `Super-administrador`) no existe → la crea para **todas** las empresas existentes. (En primer deploy hay una Empresa seed; si hay 0 empresas, el hosted service loggea warning y espera al primer `EmpresaCreadaEvent`).

4. Verificar logs:
   ```
   [INF] BootstrapSuperAdminHostedService: Super-administrador role found/created (id=...)
   [INF] BootstrapSuperAdminHostedService: User eduardo.paredes@tiglass.net found/created (id=...)
   [INF] BootstrapSuperAdminHostedService: Assigned Super-administrador to user in 1 empresas
   ```

**Verificación:**

```sql
SELECT u.email, r.nombre, e.razon_social
FROM identidad.usuarios u
JOIN identidad.usuario_empresa_rol uer ON uer.usuario_id = u.id
JOIN identidad.roles r ON r.id = uer.rol_id
JOIN compartido.empresas e ON e.id = uer.empresa_id
WHERE u.email = 'eduardo.paredes@tiglass.net';
```

Debe retornar al menos una fila con `nombre = 'Super-administrador'`.

**Si algo falla:**

- **Migración no aplicada** → `dotnet ef database update --context IdentidadDbContext` desde un runner con acceso de migración.
- **Secret no encontrado** → revisar permisos del Managed Identity de App Service contra Key Vault.
- **`UsuarioEmpresaRol` con `EmpresaId` inválido** → significa que no había empresas; crear una manualmente (ver §1.2 seed inicial) y re-startear la app.

### 1.2 Seed inicial de empresa, sucursales y departamentos

**Cuándo:** primer deploy de un ambiente, o on-boarding de un cliente nuevo en MVP.

**Quién:** owner + ingeniería de plataforma.

**Pre-requisitos:**

- Bootstrap super-admin completo (§1.1).
- Migración seed de empresa lista (en `Administracion/Infrastructure/Migrations/<ts>_EmpresaMVPSeed.cs`) o procedimiento manual vía UI cuando F-Admin-PR2.3 esté mergeado.

**Pasos (vía UI cuando F-Admin-PR2 esté disponible):**

1. Login con el super-admin.
2. Navegar a `/admin/empresas` → click en `+ Nueva empresa`.
3. Capturar datos fiscales:
   - RFC
   - Razón social
   - Nombre comercial
   - Régimen fiscal (seleccionar del catálogo SAT)
4. Guardar. Verificar que se publicó `EmpresaCreadaEvent` (revisar `compartido.integration_event_outbox`).
5. Click en la empresa creada → tab "Sucursales" → agregar sucursales via inline form.
6. Tab "Departamentos" → agregar departamentos via inline form.

**Pasos (vía SQL si UI aún no está disponible, ej. en F-Admin-PR0/PR1):**

```sql
-- Crear empresa
INSERT INTO compartido.empresas (id, rfc, razon_social, regimen_fiscal_id, activa, ...)
VALUES (gen_random_uuid(), 'MIL010101AB1', 'Millet S.A. de C.V.', <id-del-regimen-601>, true, ...);

-- Crear sucursal
INSERT INTO compartido.sucursales (id, empresa_id, clave, nombre, estatus, ...)
VALUES (gen_random_uuid(), <empresa-id>, 'CDMX', 'Ciudad de México', 'Activo', ...);

-- Crear departamento
INSERT INTO compartido.departamentos (id, empresa_id, clave, nombre, estatus, ...)
VALUES (gen_random_uuid(), <empresa-id>, 'COMP', 'Compras', 'Activo', ...);
```

**Verificación:**

- Endpoint `GET /api/v1/admin/empresas` retorna la empresa.
- `EmpresaSelector` en topbar la muestra.
- Si se creó por UI, `AuditLogEntry` registra la creación.

**Si algo falla:**

- **Régimen fiscal no encontrado** → revisar seed SAT (F-Admin-PR5.3). En dev, el seed parcial puede no tener todos los regímenes; cargar manualmente vía migración.
- **`EmpresaCreadaEvent` no publicado** → revisar outbox + dispatcher.

### 1.3 Seed inicial de roles MVP (los 7 base)

**Cuándo:** primer deploy, automático vía `BootstrapSuperAdminHostedService` (extendido en F-Admin-PR3.3).

**Quién:** automático.

**Pre-requisitos:**

- F-Admin-PR3.3 mergeado.
- Permisos canónicos seedeados (F-Admin-PR1.2 + cada feature suma los suyos).

**Pasos:** se ejecuta al startup. Idempotente (`ON CONFLICT DO NOTHING`).

**Verificación:**

```sql
SELECT nombre, descripcion FROM identidad.roles WHERE es_system = true ORDER BY nombre;
```

Debe retornar 7 filas:

| nombre | descripcion |
|---|---|
| Administrador Compras | Configura settings de Compras |
| Administrador de catálogos | CRUD catálogos globales |
| Administrador de datos maestros | CRUD proveedores y artículos |
| Administrador de identidad | CRUD usuarios y roles |
| Administrador organizacional | CRUD empresas, sucursales, departamentos |
| Auditor | Acceso read-only al área Admin |
| Super-administrador | Acceso total |

**Si algo falla:**

- **Roles duplicados** → seed no-idempotente. Revisar query del hosted service; reportar bug.
- **Roles sin permisos asignados** → el seed asigna permisos por enum `PermisosCanonicos`. Si un permiso no existe aún en `identidad.permisos` cuando corre el seed (race condition), el rol queda sin él. Mitigación: el hosted service espera a que todas las migraciones de permisos hayan corrido (`MigrationsAppliedHealthCheck`).

### 1.4 Carga inicial de catálogos SAT

**Cuándo:** primer deploy y cuando SAT publique actualizaciones.

**Quién:** ingeniería (migración aditiva).

**Pre-requisitos:** migraciones de `Catalogos` aplicadas.

**Pasos:**

- Las migraciones seed completos (F-Admin-PR5.3) cargan:
  - Formas de pago SAT (catálogo DOF)
  - Usos CFDI (Anexo 20)
  - Régimenes fiscales (Anexo 24)
  - Unidades de medida SAT (catálogo c_ClaveUnidad)
  - Incoterms básicos (EXW, FCA, FOB, CIF, DAP, DDP)

- Actualización futura: cuando SAT publique cambio, crear nueva migración aditiva con `ON CONFLICT DO NOTHING` para nuevos códigos.

**Verificación:**

```sql
SELECT COUNT(*) FROM compartido.formas_pago;        -- esperar > 10 (catálogo DOF)
SELECT COUNT(*) FROM compartido.usos_cfdi;          -- esperar ~ 30+
SELECT COUNT(*) FROM compartido.regimenes_fiscales;  -- esperar ~ 20+
SELECT COUNT(*) FROM compartido.unidades_medida;    -- esperar ~ 1000+ (catálogo SAT completo)
```

**Si algo falla:**

- **Seed parcial** → re-ejecutar migración seed. `ON CONFLICT DO NOTHING` la hace segura.

### 1.5 Carga inicial de monedas y tipos de cambio

**Cuándo:** primer deploy.

**Quién:** automático (seed) + admin de catálogos (carga de tipos de cambio del día).

**Pasos:**

- Migración seed crea Monedas base: MXN, USD, EUR.
- Admin entra a `/admin/catalogos/monedas` → selecciona USD → tab "Tipos de cambio" → inline form → registra el tipo de cambio del día.
- Repite para cada moneda extranjera relevante.

**Verificación:**

```sql
SELECT m.codigo, tc.fecha, tc.valor_en_mxn
FROM compartido.monedas m
LEFT JOIN compartido.tipos_cambio tc ON tc.moneda_id = m.id AND tc.fecha = CURRENT_DATE
ORDER BY m.codigo;
```

**Si algo falla:**

- **Tipo de cambio sin registrar para hoy** → handlers de módulos que requieran conversión van a fallar con 422 informativo. Recordatorio: el sync automático DOF/Banxico es deuda futura (`<TipoCambioSync>`).

### 1.6 Mapeo inicial de grupos Entra ID a roles

**Cuándo:** durante on-boarding de cada empresa cliente.

**Quién:** super-admin + administrador de IT del cliente (provee los object IDs de los grupos Entra ID).

**Pre-requisitos:**

- F-Admin-PR3 mergeado.
- Grupos creados en Entra ID del tenant del cliente.

**Pasos:**

1. Owner solicita al cliente la lista de grupos Entra ID que deben mapearse + sus object IDs. Sugerencia de naming:
   - `Millet_SuperAdmins` → Super-administrador
   - `Millet_Compras_Jefes` → Administrador Compras
   - `Millet_Admin_TI` → Administrador de identidad + Administrador organizacional
   - `Millet_Auditores` → Auditor
   - etc.
2. Super-admin entra a `/admin/roles` → selecciona cada rol → tab "Grupos Entra ID" → inline form → agrega object ID o busca por nombre.
3. Repite para cada rol.

**Verificación:**

```sql
SELECT r.nombre, rg.nombre AS grupo_entra_id, rg.object_id
FROM identidad.roles r
JOIN identidad.rol_grupo_entra_id rg ON rg.rol_id = r.id
ORDER BY r.nombre;
```

**Sincronización a usuarios:**

- Al next login, `LoginOrchestrator` lee los grupos del token Entra ID, los matchea contra `rol_grupo_entra_id`, y crea/actualiza `UsuarioEmpresaRol` para el usuario.
- Sync masivo (todos los usuarios de un grupo) es deuda futura — `<EntraIdMapping>` hosted service post-MVP. En MVP, los usuarios se materializan **al hacer login**.

**Si algo falla:**

- **Object ID inválido** → 422 al guardar. Verificar contra Entra ID admin center.
- **Grupo sin miembros** → no aparecen usuarios; sincroniza al primer login de cada miembro.

### 1.7 Configuración inicial de series y folios

**Cuándo:** antes de que un módulo de negocio empiece a emitir documentos en una empresa.

**Quién:** super-admin u administrador organizacional.

**Pre-requisitos:** F-Admin-PR6.1 mergeado; empresa y sucursales ya creadas.

**Pasos:**

1. Admin entra a `/admin/series` → `+ Nueva serie`.
2. Capturar:
   - Empresa (requerida)
   - Sucursal (opcional; si null, aplica cross-sucursal)
   - Tipo de documento (OC, CFDI, NotaCredito, Poliza, etc.)
   - Prefijo (texto corto, ej. "OC")
   - Sufijo (opcional)
   - Reinicio: Eterno | Anual | Mensual (A5)
3. Preview muestra próximo folio: `OC-2026-0001` (Anual), `OC-2026-05-0001` (Mensual), `OC-0001` (Eterno).
4. Guardar.

**Series mínimas MVP por empresa:**

- 1 serie de OC por sucursal (Anual, prefijo "OC-{SUCURSAL}").
- 1 serie de OC para importaciones (Anual, prefijo "OCI-{SUCURSAL}").
- 1 serie de CFDI por sucursal (Anual, prefijo "F-{SUCURSAL}") — cuando Facturación arranque.
- 1 serie de Pólizas (Mensual, prefijo "P") — cuando Contabilidad arranque.

**Verificación:**

```sql
SELECT empresa_id, tipo_documento, prefijo, reinicio_periodo, activa
FROM admin.series ORDER BY empresa_id, tipo_documento, prefijo;
```

**Si algo falla:**

- **`ReservarFolioCommand` falla con timeout** → SELECT FOR UPDATE bloqueado. Revisar transactions activas en `secuencias_folio`. Ejecutar `SELECT * FROM pg_locks WHERE relation = 'admin.secuencias_folio'::regclass`.

### 1.8 Configuración inicial de parámetros globales

**Cuándo:** primer deploy.

**Quién:** ingeniería (migración seed) + super-admin (ajustes vía UI).

**Pasos:**

- Migración seed (F-Admin-PR7.1) carga parámetros base:
  - `TimezoneDefault` = `America/Mexico_City`
  - `FormatoFecha` = `dd/MM/yyyy`
  - `RedondeoMonetario` = `2`
  - `IdiomaDefault` = `es-MX`
- Admin entra a `/admin/parametros` para ajustar si requiere.

**Verificación:**

```sql
SELECT clave, valor, tipo FROM admin.parametros_globales ORDER BY clave;
```

---

## 2. Procedimientos operativos recurrentes

### 2.1 Alta de un usuario nuevo

**Cuándo:** un colaborador entra a la empresa o se promueve a uso del ERP.

**Quién:** admin de identidad.

**Pasos:**

1. Verificar que el usuario tenga cuenta en Entra ID (responsabilidad de IT del cliente).
2. Login del admin → `/admin/usuarios` → `+ Nuevo usuario`.
3. Capturar email del usuario. La UI consulta Graph y autocompleta `EntraIdObjectId`, nombre completo.
4. Asignar departamento (opcional).
5. Tab "Roles": inline form → seleccionar empresa + rol. Agregar las combinaciones necesarias.
6. Guardar.

**Alternativa:** si el usuario está en un grupo Entra ID mapeado a un rol (§1.6), basta con que haga login — el sistema lo materializa automáticamente.

**Verificación:**

- Usuario aparece en la lista `/admin/usuarios`.
- `AuditLogEntry` registra la creación + asignaciones.
- El usuario puede hacer login y ver las pantallas según su rol.

**Si algo falla:**

- **Email ya existe** → invariante. Sugerir reactivar el usuario existente si estaba desactivado.
- **Object ID de Entra ID no resuelto** → revisar permisos de Graph API del backend (Managed Identity necesita permiso `User.Read.All`).

### 2.2 Cambio de rol de un usuario existente

**Cuándo:** promoción, cambio de área, restricción.

**Quién:** admin de identidad.

**Pasos:**

1. `/admin/usuarios/$id` → tab "Roles".
2. Revocar rol actual via botón en la fila correspondiente.
3. Asignar nuevo rol via inline form.
4. `RolPermisosActualizadosEvent` se publica → cache de autorización invalidada.

**Efecto inmediato:** el usuario pierde acceso en el siguiente request (sin necesidad de logout/login).

### 2.3 Cambio masivo de permisos a un rol existente

**Cuándo:** cambio de política, nueva feature que requiere permiso adicional.

**Quién:** admin de identidad.

**Pasos:**

1. `/admin/roles/$id` → tab "Permisos".
2. Marcar/desmarcar checkboxes en la matriz colapsable por módulo.
3. Click en "Guardar cambios". El batch es transaccional — un solo `AuditLogEntry` con el delta.
4. `RolPermisosActualizadosEvent` se publica → cache de autorización invalidada.

**Verificación:**

```sql
SELECT canonico FROM identidad.permisos p
JOIN identidad.rol_permiso rp ON rp.permiso_id = p.id
WHERE rp.rol_id = <rol-id>
ORDER BY canonico;
```

### 2.4 Desactivación de usuario

**Cuándo:** baja del colaborador, vacaciones extendidas con riesgo de comprometer cuenta.

**Quién:** admin de identidad.

**Pasos:**

1. `/admin/usuarios/$id` → click en "Desactivar usuario" en el header.
2. Confirm `<Dialog>` con razón opcional.
3. `Usuario.Activo = false`. Todas las `UsuarioEmpresaRol` mantienen su estado pero el usuario no puede hacer login.

**Invariante:** si el usuario es el **único** super-admin activo, el sistema bloquea con 422.

**Recuperación:** mismo flujo, click "Reactivar". El usuario recupera todas sus asignaciones tal como estaban.

### 2.5 Edición de un setting de módulo (PATCH)

**Cuándo:** owner o admin de módulo cambia política operativa.

**Quién:** admin del módulo correspondiente (rol con permiso `<modulo>.settings.editar` o `<modulo>.configuracion.editar`).

**Pasos (auto-renderizado, A7=b):**

1. `/admin/<modulo>/settings` → editar el campo correspondiente.
2. Si el setting tiene `AlertaCambio`, aparece confirm con el texto.
3. Click "Guardar". PATCH con `Idempotency-Key`. `AuditLogEntry` registra antes/después.

**Pasos (UI custom, ej. Compras ADR-0033):**

1. `/admin/compras` (card en landing) → linkea a `/compras/configuracion`.
2. UI dedicada del módulo con advertencias contextuales.
3. PATCH al endpoint específico del módulo.

**Verificación:**

```sql
SELECT modulo, recurso, accion, detalle, fecha_utc
FROM compartido.audit_log_entries
WHERE modulo = '<modulo>' AND accion = 'PATCH_SETTING'
ORDER BY fecha_utc DESC LIMIT 10;
```

### 2.6 Cambio de tipo de cambio del día

**Cuándo:** cada día hábil que el módulo de negocio requiera multimoneda (Facturación, CxP con proveedores en USD, etc.).

**Quién:** admin de catálogos.

**Pasos:**

1. `/admin/catalogos/monedas` → seleccionar USD (o moneda extranjera).
2. Tab "Tipos de cambio" → inline form → fecha (hoy por default) + valor en MXN + origen (`Manual` por default).
3. Guardar.

**Verificación:**

```sql
SELECT fecha, valor_en_mxn, origen
FROM compartido.tipos_cambio
WHERE moneda_id = (SELECT id FROM compartido.monedas WHERE codigo = 'USD')
ORDER BY fecha DESC LIMIT 5;
```

**Si algo falla:**

- **Sin tipo de cambio para hoy** → handlers que requieran conversión retornan 422 con `"No hay tipo de cambio registrado para MXN/USD en la fecha 2026-05-13"`.
- **Sync automatizado DOF/Banxico** → diferido. Cuando exista (`<TipoCambioSync>`), `origen` cambia a `DOF` o `Banxico` automáticamente.

### 2.7 Consulta de bitácora de auditoría

**Cuándo:** investigación de incidente, auditoría fiscal/operativa, troubleshooting.

**Quién:** auditor o super-admin (cualquiera con `admin.auditoria.leer`).

**Pasos:**

1. `/admin/auditoria`.
2. **Capturar rango de fechas obligatorio** (P0 §4.4 del 04-cuidados; máximo 90 días).
3. Filtros opcionales: módulo, recurso, acción, usuario, empresa.
4. Click "Aplicar".
5. Click en una fila → drawer con diff JSON antes/después.

**Limitación:** rango > 90 días bloqueado por frontend + backend. Para análisis histórico extenso, generar reporte ad-hoc desde BD (deuda futura).

---

## 3. Procedimientos de recuperación

### 3.1 Se eliminó el último super-administrador

**Síntoma:** super-admin se desactivó a sí mismo (no debería pasar — hay invariante); o un bug permitió eliminarlo; o se eliminó la fila `UsuarioEmpresaRol` directamente en BD.

**Severidad:** P0 — nadie puede entrar al área Admin.

**Recuperación:**

1. Conectar a la BD con credenciales de plataforma (Key Vault `PostgresAdminPassword`).
2. Identificar la última fila `UsuarioEmpresaRol` con rol `Super-administrador`:
   ```sql
   SELECT * FROM identidad.usuario_empresa_rol uer
   JOIN identidad.roles r ON r.id = uer.rol_id
   WHERE r.nombre = 'Super-administrador' AND uer.deleted_at IS NULL;
   ```
3. Si no hay ninguna, re-asignar manualmente:
   ```sql
   INSERT INTO identidad.usuario_empresa_rol (usuario_id, empresa_id, rol_id, fecha_asignacion)
   VALUES (
     (SELECT id FROM identidad.usuarios WHERE email = 'eduardo.paredes@tiglass.net'),
     (SELECT id FROM compartido.empresas LIMIT 1),
     (SELECT id FROM identidad.roles WHERE nombre = 'Super-administrador'),
     NOW()
   );
   ```
4. Restart de la app (para invalidar cache de autorización).
5. Verificar login.

**Prevención:** invariante en `Usuario.Desactivar()` y `RevocarRolDeUsuarioCommand` (F-Admin-PR4.1) bloquea con 422 si el último super-admin sería desactivado. Tests cubren el caso.

### 3.2 Permisos canónicos desincronizados (código vs BD)

**Síntoma:** endpoint nuevo retorna 500 con `Permiso 'xxx' no encontrado en identidad.permisos`.

**Causa:** se agregó constante a `PermisosCanonicos.cs` pero la migración seed no se ejecutó.

**Recuperación:**

1. Verificar migraciones pendientes:
   ```bash
   dotnet ef migrations list --context IdentidadDbContext
   ```
2. Aplicar migraciones:
   ```bash
   dotnet ef database update --context IdentidadDbContext
   ```
3. Confirmar que el permiso existe:
   ```sql
   SELECT canonico FROM identidad.permisos WHERE canonico = 'xxx';
   ```
4. Restart no es necesario — los permisos se consultan por request.

**Prevención:** P0 §3.1 del 04-cuidados. Test integration valida `identidad.permisos` contiene todos los `PermisosCanonicos.Todos`.

### 3.3 Migración falló a mitad

**Síntoma:** `dotnet ef database update` falló; algunas migraciones aplicaron, otras no. `/health/ready` 503.

**Recuperación:**

1. Identificar último migration aplicado:
   ```sql
   SELECT migration_id FROM compartido."__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 5;
   ```
2. Identificar el primer migration que falla:
   ```bash
   dotnet ef migrations list --context <DbContext>
   ```
3. Revisar el SQL del migration fallido (`dotnet ef migrations script <prev> <failed>`).
4. Si es un seed con violación de constraint, revisar idempotencia (`ON CONFLICT DO NOTHING`).
5. Aplicar migration manualmente:
   ```bash
   dotnet ef database update --context <DbContext> --target <failed_migration>
   ```
6. Si la migration es destructiva y falla, **NO** revertir; consultar con el reviewer original.

**Prevención:** ADR-0005, todos los seeds idempotentes (P0 §2.3 del 04-cuidados).

### 3.4 Setting de módulo quedó con valor inválido

**Síntoma:** PATCH a un setting introdujo un valor que rompe el handler que lo consume (ej. `RedondeoMonetario = -1`).

**Causa:** validación frontend incompleta + validación backend falló (no debería pasar — la validación se hace en `SettingsSchemaProvider`).

**Recuperación:**

1. Identificar el setting:
   ```sql
   SELECT * FROM compartido.audit_log_entries
   WHERE accion = 'PATCH_SETTING' AND detalle->>'after' LIKE '%invalido%'
   ORDER BY fecha_utc DESC LIMIT 5;
   ```
2. Revertir al valor anterior:
   ```sql
   UPDATE <modulo>.settings SET <campo> = <valor_anterior>, version = version + 1, updated_at = NOW()
   WHERE empresa_id = '<empresa-id>';
   ```
3. Restart no necesario.
4. Reportar bug en el validator del provider correspondiente.

**Prevención:** P1 §11.3 del 04-cuidados. Tests por tipo de setting.

### 3.5 Cache de autorización stale tras cambio de rol

**Síntoma:** usuario cambia de rol pero sigue viendo / no ve las opciones esperadas.

**Causa:** `RolPermisosActualizadosEvent` o `UsuarioRolAsignadoEvent` no se publicó/consumió.

**Recuperación:**

1. Verificar evento publicado:
   ```sql
   SELECT * FROM compartido.integration_event_outbox
   WHERE event_type IN ('RolPermisosActualizadosEvent', 'UsuarioRolAsignadoEvent')
   ORDER BY created_at DESC LIMIT 10;
   ```
2. Si el evento está pero no se procesó (`processed_at IS NULL`), forzar dispatcher (en dev: restart de la app).
3. Si el evento no se publicó, revisar handler del command. Posible bug.
4. **Workaround inmediato para el usuario:** logout/login.

**Prevención:** §6.3 y §8.1 del 04-cuidados. Tests integration verifican publicación.

### 3.6 Folio duplicado (CRÍTICO)

**Síntoma:** dos OCs (o documentos) con el mismo folio. Auditoría fiscal en riesgo.

**Severidad:** P0.

**Causa:** race condition en `ReservarFolioCommand`. Test §13.2 del 04-cuidados (50 reservas paralelas) debió detectarlo.

**Recuperación:**

1. **No corregir el folio en BD** — sería un cambio retroactivo no auditado.
2. Identificar las filas afectadas:
   ```sql
   SELECT folio, COUNT(*) FROM compras.ordenes_compra GROUP BY folio HAVING COUNT(*) > 1;
   ```
3. Para cada folio duplicado:
   - Decidir cuál OC es "real" (la primera por `created_at`).
   - La duplicada se **cancela** vía `CancelarOrdenCompraCommand` con razón "Folio duplicado por bug en reserva".
   - Crear una **nueva** OC con un nuevo folio (siguiente correlativo) replicando los datos.
4. Reportar incidente al owner. Investigar causa raíz en `ReservarFolioCommand`.

**Prevención:** P0 §5.3 del 04-cuidados (SELECT FOR UPDATE atómico). Test §13.2 del 04-cuidados.

### 3.7 La empresa quedó sin `ComprasSettings`

**Síntoma:** al autorizar RQ, falla con FK violation o `ComprasSettings not found`.

**Causa:** `EmpresaCreadaEvent` no se consumió por el listener de Compras (ADR-0033 + §8.3 del 04-cuidados).

**Recuperación:**

1. Verificar fila:
   ```sql
   SELECT * FROM compras.settings WHERE empresa_id = '<empresa-id>';
   ```
2. Si no existe, crear manualmente con defaults:
   ```sql
   INSERT INTO compras.settings (id, empresa_id, auto_generar_oc_al_autorizar, version, created_at, updated_at)
   VALUES (gen_random_uuid(), '<empresa-id>', false, 1, NOW(), NOW());
   ```
3. Verificar que el listener procesa eventos. En dev: revisar logs por errores en `EmpresaCreadaEventListener`.
4. Reportar bug.

**Prevención:** P0 §8.3 del 04-cuidados. Test integration crea empresa y verifica `ComprasSettings` materializada.

---

## 4. Procedimientos de rotación

### 4.1 Rotación de roles (auditoría periódica)

**Cuándo:** cada trimestre o ante cambio organizacional mayor.

**Quién:** auditor + owner.

**Pasos:**

1. Auditor genera reporte: `/admin/usuarios` → exportar (deuda futura) o consulta SQL:
   ```sql
   SELECT u.email, u.nombre_completo, r.nombre AS rol, e.razon_social
   FROM identidad.usuarios u
   JOIN identidad.usuario_empresa_rol uer ON uer.usuario_id = u.id
   JOIN identidad.roles r ON r.id = uer.rol_id
   JOIN compartido.empresas e ON e.id = uer.empresa_id
   WHERE u.activo = true AND uer.deleted_at IS NULL
   ORDER BY e.razon_social, r.nombre, u.email;
   ```
2. Owner revisa con el responsable de cada área.
3. Cambios identificados se aplican vía UI estándar (§2.2, §2.3, §2.4).
4. Toda revocación queda auditada.

### 4.2 Revisión periódica de permisos canónicos

**Cuándo:** cada release mayor o ante cambio de modelo de negocio.

**Quién:** owner + tech lead.

**Pasos:**

1. Revisar `PermisosCanonicos.cs` — todos los `public const string` deben:
   - Seguir convención `<modulo>.<recurso>.<accion>`.
   - Tener al menos un rol asignado en `BootstrapSuperAdminHostedService` (a Super-administrador como mínimo).
   - Estar referenciados al menos en un endpoint vía `[RequirePermission(...)]`.
2. Permisos "huérfanos" (sin uso) son candidatos a deprecación. Documentar y eliminar en migración separada.

---

## 5. Diagnóstico rápido

### 5.1 "No veo el engrane en el topbar"

- Verificar que el usuario tenga **al menos un** permiso en el registry (`useAdminAccess()` retorna true).
- Si tiene permisos pero el engrane no aparece, F12 → consola → revisar logs del hook.
- Si tiene permisos en BD pero `useAuth()` no los expone, la sesión está stale — logout/login.

### 5.2 "Al hacer click en una card, va a 404"

- La ruta declarada en el card no está registrada en TanStack Router. Verificar `frontend/src/routes/admin/...` existe.
- Si el módulo aún no implementó su UI (ej. Facturación), la card no debería estar en el registry todavía. Removerla del barrel.

### 5.3 "El form auto-renderizado de settings no muestra nada"

- Verificar que el módulo expone `GET /api/v1/<modulo>/settings/schema` (debe retornar items con `Mostrar = Auto`).
- Si solo tiene `Mostrar = Custom`, el form genérico está vacío pero debería mostrar el link a `RutaCustom`.

### 5.4 "Al crear empresa, no se publica `EmpresaCreadaEvent`"

- Revisar `compartido.integration_event_outbox` con `event_type = 'EmpresaCreadaEvent'`.
- Si la fila existe pero `processed_at IS NULL`, el dispatcher no procesa. Restart de la app (dev).
- Si la fila no existe, el handler no llamó al publisher. Bug.

### 5.5 "El bot de auto-merge bloqueó mi PR de admin/*"

- Verificar `gh pr checks <pr#>`: todos los buckets deben ser `pass` o `skipping`.
- Si hay `pending`, esperar a que termine (`gh pr checks --watch`).
- Si hay `fail`, investigar el log del workflow (`gh run view <run-id>`).
- Si el branch no matchea `^admin/`, el hook bloquea con `exit 2`. Renombrar branch.

---

## 6. Dependencias de plataforma pendientes (referencia)

Resumen de los `PLATFORM-TODO` que aplican a Admin (heredados del 01-diseño §12 y 02-plan §7):

| Identificador | Estado actual | Cuándo cerrar |
|---|---|---|
| `<EntraIdMapping>` | Manual via UI (A3=a) | Post-MVP cuando los grupos Entra ID sean estables |
| `<TipoCambioSync>` | Carga manual via UI (A4=a) | Post-MVP cuando se priorice |
| `<SchemaRename>` | Schemas físicos siguen siendo `compartido` (Fase A) | Sin fecha; evaluable cuando exista consumidor externo |
| `<AuditUI>` | Cerrado en F-Admin-PR7.2 | ✅ |
| `<SettingsAutoRender>` | Cerrado en F-Admin-PR1.1 (A7=b) | ✅ |
| `<FolioSecuenciaDeprecate>` | `Compras.FolioSecuencia` deprecada en F-Admin-PR6.2 | Cuando todos los módulos consuman `ReservarFolioCommand` |
| `<EntraIdResolver>` | `LocalEntraIdResolverNoOp` en F-Admin-PR4.1 | Cuando se wire Graph API real |
| `<EntraDirectorio>` | `DirectorioEntraSimulado` (en memoria) para el alta unificada, plan F1-ADM-01 F2 | Cuando TI entregue `User.ReadWrite.All` en la App Registration: adaptador Graph de `IEntraDirectorioPort` (`GET /users`, `POST /users`) |
| `<ServiceBusWireup>` | In-process via MediatR | Cuando Notificaciones/Almacén/CxP existan y requieran cross-process |
| `<CatalogosSapImport>` | Seeds versionados solo | Post-MVP si el cliente requiere migración masiva |

---

## 7. Contacts y escalamiento

| Severidad | Quién | SLA |
|---|---|---|
| P0 (sistema down, super-admin inaccesible, folio duplicado) | Owner (Eduardo Paredes) | inmediato |
| P1 (cambios de permisos no efectivos, settings inválidos) | Tech lead + owner | 4 horas |
| P2 (UI inconsistente, queries lentas) | Tech lead | siguiente día hábil |

---

## Rev.

- **2026-05-13** — Rev. 1. Runbook inicial. Autor: Claude.
