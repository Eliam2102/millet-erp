# Plan de Cierre de Brechas · Sección 3: Contabilidad, Balanza y Pólizas Automáticas

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cerrar las brechas al 100% y sin simulaciones ni datos provisionales de las 4 tareas de ClickUp de la Sección 3 (`86e3za6ck`, `86e3zcbjw`, `86e3z5w25`, `86e3yyy8t`) en `millet-erp`.

**Architecture:** Incorporación de cuentas oficiales y mapeo de roles en migraciones EF Core de `Millet.Contabilidad`, configuración de cuenta puente `201.05.00.00`, catálogo de 13 retenciones completas sin cuentas nulas, resolución automática de Dim1 (sucursal) y Dim2 por omisión con exención de Dim3 para pólizas automáticas a 500/600 en `MotorPolizas`/`ValidadorDimensiones`, y verificación de cuadre de almacén contra cuentas reales.

**Tech Stack:** .NET 10 / C# 13 / Entity Framework Core / PostgreSQL (Npgsql) / xUnit

**Spec:** [Ficha Técnica 11 · Contabilidad](file:///home/devgeo/Obsidian/Millet-ERP/14%20Estado%20por%20m%C3%B3dulo/11%20%C2%B7%20Contabilidad%20%C2%B7%20estado%20y%20conexiones.md) y correos de acuerdos con Laura Cerón y Fernando Alejos (5 y 6-nov-2026).

---

## Global Constraints

- Prohibido dejar valores provisionales (`990.*`, `DEMO-PUENTE`, cuentas dummy) en roles productivos.
- Las cuentas contables deben registrarse con código agrupador SAT y naturaleza contable oficial.
- Cero advertencias de compilación y 100% de pruebas unitarias e integración pasando.
- Respetar el aislamiento de multiempresa y permisos existentes.

---

## Review Focus

1. **Rechazo por rol sin cuenta:** Ningún evento contable automático debe generar rechazo `RolSinCuenta` para operaciones estándar.
2. **Rechazo C-03 por Dim2/Dim3:** Ninguna póliza automática a cuentas 500/600 debe quedar bloqueada por falta de Dim2 (Área) o Dim3 (Equipo).
3. **Mapeo de las 13 retenciones:** Cada una de las 13 retenciones de Laura Cerón debe imputar a su cuenta contable oficial.
4. **Cuenta puente de compras:** La cuenta puente oficial debe ser `201.05.00.00` en lugar de `115.05.00.00` o `DEMO-PUENTE`.
5. **Cuadre de inventario:** El reporte de Almacén no debe mostrar la etiqueta "Sin cuenta configurada" para los grupos de inventario.

---

### Task 1: [86e3za6ck] Migración EF Core de Cuentas Reales y Asignación de Roles del Motor

**Files:**
- Create: `backend/src/Contabilidad/Infrastructure/Persistence/Migrations/20261107170000_CuentasOficialesYRolesContabilidad.cs`
- Modify: `backend/src/Api/appsettings.json`
- Modify: `backend/src/Api/appsettings.Development.json`
- Modify: `backend/src/Api/Seed/DemoSesionRolesContables.cs`
- Test: `backend/tests/Contabilidad.UnitTests/Motor/CuentasOficialesRolesTests.cs`

- [ ] **Step 1: Escribir prueba unitaria que verifique la asignación y resolución de cuentas oficiales en roles del motor**
- [ ] **Step 2: Ejecutar prueba para verificar el fallo esperado**
- [ ] **Step 3: Crear migración EF Core que dé de alta en `contabilidad.cuentas_contables` las cuentas oficiales (`101.01.03.00`, `101.01.04.00`, `201.05.00.00`, `201.06.00.00`, `504.25.02.00`, `704.23.02.00`) y actualice los roles del motor (`MERCANCIA_RECIBIDA_POR_FACTURAR`, `COBROS_TC_TD`, `COBROS_CHEQUE`, `MERMA`, `AJUSTE_INVENTARIO`)**
- [ ] **Step 4: Actualizar `appsettings.json` y `appsettings.Development.json` con `Contabilidad:CuentaPuente:CodigoCuenta = "201.05.00.00"`**
- [ ] **Step 5: Ejecutar pruebas unitarias para verificar que pasen**
- [ ] **Step 6: Commit de Task 1**

---

### Task 2: [86e3zcbjw] Catálogo de 13 Retenciones Oficiales y Mapeo Completo sin Cuentas Nulas

**Files:**
- Modify: `backend/src/Contabilidad/Infrastructure/Persistence/Migrations/20261107170000_CuentasOficialesYRolesContabilidad.cs`
- Modify: `backend/src/Contabilidad/Application/Motor/RetencionesContables.cs`
- Test: `backend/tests/Contabilidad.UnitTests/Motor/RetencionesOficialesTests.cs`

- [ ] **Step 1: Escribir pruebas unitarias que verifiquen que los 13 conceptos de retención resuelven cuentas oficiales sin caer en rol nulo**
- [ ] **Step 2: Ejecutar prueba para verificar el fallo**
- [ ] **Step 3: Agregar en la migración las cuentas de retención faltantes (`216.02.04.00`, `216.01.05.00`, `216.02.02.02`, `216.02.03.00`, `113.01.02.07`) y asignar los roles `RETENCIONES_POR_PAGAR_*` y `RETENCIONES_CLIENTES_*`**
- [ ] **Step 4: Actualizar `RetencionesContables.cs` para contemplar todos los roles de la tabla oficial**
- [ ] **Step 5: Ejecutar pruebas y verificar que pasen al 100%**
- [ ] **Step 6: Commit de Task 2**

---

### Task 3: [86e3z5w25] Resolución de Dim1/Dim2 y Exención de Dim3 para Pólizas Automáticas en Cuentas 500 y 600

> **Revertida por M-453 (86e3zr740):** Contabilidad confirmó que el usuario captura área y equipo. El motor ya no asigna área por omisión ni exime el equipo en cuentas 600; la póliza automática sin la dimensión que exige la regla C-03 va a Rechazos («Dato faltante»). Se conserva la ubicación derivada de la sucursal.

**Files:**
- Modify: `backend/src/Contabilidad/Application/Motor/MotorPolizas.cs`
- Modify: `backend/src/Contabilidad/Application/Dimensiones/ValidadorDimensiones.cs`
- Test: `backend/tests/Contabilidad.UnitTests/Motor/PolizasAutomaticasDimensionesTests.cs`

- [ ] **Step 1: Escribir prueba unitaria que verifique que una póliza automática a cuentas 500/600 (importación, comisiones, viáticos, mermas) resuelve Dim1 y Dim2 y exenta Dim3 sin rechazo**
- [ ] **Step 2: Ejecutar prueba para verificar el fallo**
- [ ] **Step 3: Modificar `MotorPolizas.cs` para resolver Dim1 desde la sucursal del evento y Dim2 por omisión según el módulo emisor cuando la cuenta pertenezca a 500 o 600**
- [ ] **Step 4: Modificar `ValidadorDimensiones.cs` para exentar Dim3 en pólizas automáticas a cuentas 600 sin maquinaria física**
- [ ] **Step 5: Ejecutar pruebas unitarias para confirmar que pasen**
- [ ] **Step 6: Commit de Task 3**

---

### Task 4: [86e3yyy8t] Cuentas de Inventario Reales y Conciliación Almacén-Contabilidad

**Files:**
- Modify: `backend/src/Contabilidad/Infrastructure/Persistence/Migrations/20261107170000_CuentasOficialesYRolesContabilidad.cs`
- Test: `backend/tests/Api.IntegrationTests/Contabilidad/CuentaInventarioAlm13Tests.cs`

- [ ] **Step 1: Asegurar en la migración que todos los roles `INVENTARIO_{grupo}` apunten a las cuentas reales de inventario (`115.02.01.00`, `115.02.02.00`, `115.02.03.00`, `115.07.02.00`)**
- [ ] **Step 2: Ejecutar `CuentaInventarioAlm13Tests` y verificar que el reporte responda sin errores**
- [ ] **Step 3: Commit de Task 4**

---

### Task 5: Actualización de la Bóveda Obsidian y Reporte para ClickUp

**Files:**
- Modify: `/home/devgeo/Obsidian/Millet-ERP/14 Estado por módulo/00 · Auditoría de tareas ClickUp cerradas y huecos.md`
- Modify: `/home/devgeo/Obsidian/Millet-ERP/14 Estado por módulo/11 · Contabilidad · estado y conexiones.md`

- [ ] **Step 1: Actualizar `00 · Auditoría de tareas ClickUp cerradas y huecos.md` cambiando de 🟡 a 🟢 las tareas `86e3za6ck`, `86e3zcbjw`, `86e3z5w25` y `86e3yyy8t`, y ajustando los totales de tareas cerradas limpias**
- [ ] **Step 2: Actualizar `11 · Contabilidad · estado y conexiones.md` pasando las tareas a la tabla de tareas cerradas y verificadas**
- [ ] **Step 3: Generar reporte técnico final con evidencias para documentar en ClickUp**
- [ ] **Step 4: Commit de documentación en bóveda**
