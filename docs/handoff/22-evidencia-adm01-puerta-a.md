# Reporte Formal de Evidencia Técnica — Puerta A (Cierre ADM-01)

- **Fecha:** 2026-09-24 19:40 CST
- **Rama Git:** `F1-ADM-01`
- **Commit Congelado de QA (Paso C1):** `1f128f7` (`chore(dev): tests fuera de la BD de desarrollo y datos demo confiables`)
- **Documento de Aceptación:** `.local-context/Documentos/Alcance_y_aceptacion_cierre_ADM_01_ADM_02_BORRADOR.md`
- **Ambiente:** Local (PostgreSQL 16 en contenedor aislado, Mailpit SMTP local en puerto 8025, API en puerto 5000, Vite en puerto 5173, Microsoft Graph conectado a tenant de desarrollo `uzieltzaboutlook.onmicrosoft.com`).

---

## 1. Estado de Puertas de Aceptación (§7)

| Puerta | Estado | Evidencia / Resumen | Responsable y Validación |
|---|---|---|---|
| **A. QA de Desarrollo** | **Aprobada** | Build limpio (0 errores / 0 advertencias), 366 unit tests, 644 tests de integración en PG aislado, 123 tests de frontend, eslint 0 errores, build frontend exitoso, smoke test automatizado y reporte SQL sin inconsistencias críticas. | Antigravity / Claude Code (2026-09-24) |
| **B. Integración Real Entra ID** | **Verificada en Dev / Pendiente Tenant Corporativo** | Alta de cuenta y provisión completada exitosamente vía Microsoft Graph real contra el tenant de desarrollo (`@uzieltzaboutlook.onmicrosoft.com`). Pendiente la app single-tenant definitiva y buzones en el tenant corporativo de Millet. | TI Millet / En proceso |
| **C. UAT y Negocio** | **Pendiente** | Validación final de flujos y carga de catálogos definitivos de sucursales, departamentos y puestos con el área de Administración. | Administración Millet |

---

## 2. Métricas y Ejecución de Suites Completas (Paso C2)

Todas las pruebas fueron ejecutadas contra el commit `1f128f7`:

### 2.1 Backend Build (`-warnaserror`)
- **Comando:** `dotnet build backend/Millet.sln -warnaserror`
- **Resultado:** Compilación correcta. **0 Advertencia(s), 0 Errores**.

### 2.2 Pruebas Unitarias Backend
- **Comando:** `dotnet test backend/tests/{Administracion,Identidad,SharedKernel}.UnitTests`
- **Conteos:**
  - `Administracion.UnitTests`: **124 superadas**, 0 errores.
  - `Identidad.UnitTests`: **71 superadas**, 0 errores.
  - `SharedKernel.UnitTests`: **171 superadas**, 0 errores.
  - **Total Unitarias:** **366 pruebas superadas**, 0 errores.

### 2.3 Gate de Integración en PostgreSQL Aislado
- **Comando:** `./tools/validate-integration-isolated.sh`
- **Aislamiento:** Contenedor Docker temporal con base de datos limpia e independiente de `millet_dev`.
- **Conteos:**
  - `Integraciones.Aw.IntegrationTests`: **7 superadas**, 0 errores.
  - `Compras.IntegrationTests`: **120 superadas**, 0 errores.
  - `Api.IntegrationTests`: **517 superadas**, 0 errores.
  - **Total Integración:** **644 pruebas superadas**, 0 errores.

### 2.4 Frontend (Administración y Autenticación)
- **Vitest:** `npm --prefix frontend run test -- --run src/modules/administracion src/modules/auth`
  - **29 archivos de prueba superados**, **123 pruebas superadas**, 0 errores.
- **Linter:** `npm --prefix frontend run lint`
  - **0 errores** (8 advertencias informativas de memoización React).
- **Compilación de Producción:** `npm --prefix frontend run build`
  - Compilación exitosa en **1.90s**, bundle generado en `dist/`.

---

## 3. Recorrido Funcional Smoke Test y Reconciliación SQL (Paso C3)

### 3.1 Smoke Test Automatizado Local
- **Script:** `./tools/smoke-adm-local.sh`
- **Ejecución:**
  ```bash
  ADM_API_URL=http://127.0.0.1:5000 ADM_UPN_DOMAIN=uzieltzaboutlook.onmicrosoft.com ./tools/smoke-adm-local.sh
  ```
- **Flujo comprobado:**
  1. Autenticación administrativa con usuario SuperAdmin.
  2. Creación de sucursal (`QA-9e8b380c`) y departamento (`QD-9e8b380c`) con vinculación mutua.
  3. Creación de puesto (`QP-9e8b380c`) con rol sugerido (`admin-identidad`) y vinculación a sucursal + departamento.
  4. Alta unificada de colaborador (`QC-9e8b380c`) en modo acceso con provisión nueva:
     - `EmpleadoId`: `01a0d637-b3ca-709f-aa10-28618b4fdad7`
     - `UsuarioId`: `01a0d637-b3b9-7103-b096-6b7bf3c74600`
     - `SucursalId`: `01a0d637-b195-73ad-87ee-0a732f9127d0`
  5. Provisión real en Microsoft Graph: cuenta creada (`qa-9e8b380c@uzieltzaboutlook.onmicrosoft.com`) con estado de acceso `1` (Pendiente de primer ingreso).
  6. Envío de correo local seguro: capturado en Mailpit (`qa-9e8b380c@example.test`) con clave temporal y enlace de acceso, sin salida a Internet.
  7. Simulación de primer acceso y validación de permisos: paso a estado `0` (Activo) con 222 permisos evaluados.

### 3.2 Reporte de Reconciliación SQL (`tools/reporte-reconciliacion-colaboradores.sql`)
- **Ejecución:** `psql -d millet_dev -f tools/reporte-reconciliacion-colaboradores.sql`
- **Resultado:**
  - **Categoría A (Colaborador con cuenta activa y OID resuelto):** 3
  - **Categoría C (Empleados sin cuenta ERP creada / datos demo):** 16
  - **Categoría D (Cuentas sin empleado / técnicos y SuperAdmin):** 2
  - **Categoría E (Usuarios en proceso / vinculación pendiente):** 1
  - **Inconsistencias críticas (tabla 2):** **0 filas**. Base de datos íntegra.

---

## 4. Matriz de Cumplimiento de Criterios de Aceptación (01-01 al 01-14)

| Criterio | Descripción del Criterio | Estado | Evidencia de Validación / Test Automatizado |
|---|---|---|---|
| **01-01** | **Empresa Millet:** Gestión controlada, validación de obligatorios, duplicados y desactivación controlada. | **Cumplido** | `EmpresasEndpointsTests.cs`, `EmpresasServiceTests.cs`. RFC y duplicados validados con 409/422. |
| **01-02** | **Sucursales:** Creación, edición, desactivación solo sin dependencias operativas activas. | **Cumplido** | `SucursalesEndpointsTests.cs`, tests de bloqueo de desactivación con empleados/almacenes activos. |
| **01-03** | **Departamentos y puestos por sucursal:** Asignación explícita, historial y desacoplamiento estructural. | **Cumplido** | `SucursalDepartamentosEndpointsTests.cs`, `SucursalPuestoTests.cs`. Historial comprobado en Parte B3. |
| **01-04** | **Puestos y roles sugeridos:** Rol sugerido solo como guía visual (hint); alta y dar acceso exigen rol explícito (400 si omite). Soporte de puesto genérico en múltiples departamentos (Parte E). | **Cumplido** | Migración `20260925000746_PuestoEnVariosDepartamentosPorSucursal`, `SucursalPuestoVariosDepartamentosTests.cs`, `PuestoSelector.tsx`. |
| **01-05** | **Contexto de sucursal activa:** Selector de sucursal operativa, persistencia en sesión, aislamiento en auditoría y administración. | **Cumplido** | `AuditoriaPage.tsx`, `SucursalSelector.test.tsx`, `useEmpleadosAdmin.ts`, endpoints `/api/auth/sucursales`. |
| **01-06** | **Sucursal laboral y operativa:** Empleado conserva sucursal laboral fija; usuario puede tener múltiples sucursales operativas independientes. | **Cumplido** | `ColaboradoresEndpointsTests.cs`, pruebas de sincronización de `usuario_sucursales` sin alterar sucursal de adscripción. |
| **01-07** | **Alta de empleado sin acceso:** Wizard guarda empleado sin crear cuenta; posterior «Dar acceso» vincula sin duplicar. | **Cumplido** | `ColaboradoresEndpointsTests.cs` (Camino A), `EmpleadoInlineForm.tsx`. |
| **01-08** | **Alta con cuenta existente:** Vinculación de usuario Microsoft existente con OID real y asignación de rol/sucursal sin secuestro de cuentas. | **Cumplido** | `ColaboradoresEndpointsTests.cs` (Camino B), validación de error 422 `USUARIO_EMAIL_YA_VINCULADO_OTRO_OID`. |
| **01-09** | **Alta con cuenta nueva:** Wizard enruta a provisión, correo capturado en Mailpit, desacoplamiento con Graph tras commit. | **Cumplido** | `ProvisionCuentaEntraWorker.cs`, `CorreoSandboxLocal.cs`, comprobado en vivo en Smoke Test con tenant real de dev. |
| **01-10** | **Atomicidad:** Fallos en datos, roles inexistentes o sucursales ajenas rollbackean la transacción completa dejando 0 filas huérfanas. | **Cumplido** | `ColaboradoresEndpointsTests.cs` (pruebas de fallo transaccional con SaveChanges rollback, Parte B5). |
| **01-11** | **Ficha única:** Master-Detail (`/admin/empleados/$id`) con pestañas separadas (Datos, Puesto/Depto, Sucursales, Roles/Accesos). | **Cumplido** | `EmpleadoDetallePage.tsx`, `CuentasAccesoList.tsx` distingue "Empleado vinculado" vs "Sin empleado vinculado". |
| **01-12** | **Cambios y bajas:** Baja laboral desactiva accesos y cancela provisión; recontratación no reactiva accesos automáticamente. | **Cumplido** | `ColaboradoresEndpointsTests.cs`, `EmpleadosEndpoints.cs` (Patch/Baja/Reactivación). |
| **01-13** | **Permisos de Administración y Alcance de Sucursal:** Operaciones acotadas a la sucursal activa; bloqueo cruzado con 403. Permiso especial transversal `admin.empleados.gestionar-todas-sucursales`. | **Cumplido** | `EmpleadoSucursalScope.cs`, `EmpleadoSucursalScopeTests.cs` (8 pruebas unitarias/integración de rechazo 403). |
| **01-14** | **Auditoría y conciliación:** Bitácora registra actor, sucursal, entidad y evento; reporte SQL clasifica casos A–G sin mutación destructiva. | **Cumplido** | `AuditLogEntry`, vista `/admin/auditoria` con filtro por sucursal, `tools/reporte-reconciliacion-colaboradores.sql`. |

---

## 5. Recorridos Integrales (§6)

- **R1 (Alta sin acceso → Dar acceso):** Verificado en suite de integración y pruebas de componentes.
- **R2 (Alta con cuenta existente):** Verificado con adopción controlada de OID y rechazo de conflictos.
- **R3 (Alta con cuenta nueva y provisión Graph):** Verificado en caliente mediante `./tools/smoke-adm-local.sh` con el tenant de desarrollo (`uzieltzaboutlook.onmicrosoft.com`) y entrega a Mailpit.
- **R4 (Aislamiento de sucursal cruzada):** Verificado con `EmpleadoSucursalScope` y respuesta 403 `SUCURSAL_FUERA_DE_ALCANCE`.
- **R5 (Baja laboral y ciclo de vida):** Verificado; la baja inactiva el usuario vinculado sin revivirlo en recontratación sin decisión explícita.
- **R6 (Atomicidad y ausencia de inconsistencias):** Verificado con 0 inconsistencias en el reporte SQL.
