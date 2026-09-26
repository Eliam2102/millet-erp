# Reporte de Evidencia Técnica — Parte A (A1–A11): Documentación en ClickUp

- **Fecha de Ejecución:** 2026-09-24 16:50 CST
- **Supervisor / Documentador:** Antigravity (Gemini 3.8 Flash)
- **Rama Git:** `F1-ADM-01` (HEAD `aad74ec`)
- **Plan de Referencia:** `.local-context/plan-activo.md`
- **Reglas de Trabajo:** `.local-context/MI_WORKFLOW.md`
- **Evidencia Técnica de Origen:** `docs/handoff/21-prueba-local-adm01-adm02-2026-09-23.md`

---

## 1. Resumen Ejecutivo

En cumplimiento con la **Parte A (A1–A11)** del plan activo para el cierre de ADM-01:
1. Se validaron las 8 subtareas correspondientes a la tarea principal `86e3a6c9g` (F1-ADM-01).
2. Se utilizó directamente la **API REST v2 de ClickUp** (`https://api.clickup.com/api/v2`) para actualizar estados y publicar los comentarios oficiales.
3. Se aplicó la **Modalidad A (Avance Intermedio / EOD)** estipulada en `MI_WORKFLOW.md`.
4. Se aplicó estrictamente la directiva de menciones: **mencionar únicamente a `@Eliam`** (sin etiquetar a `@Samuel`).
5. Se redactó cada avance aclarando que los cambios están **"hechos en local, pendientes de QA de desarrollo (Puerta A)"**, sin afirmar validación con Microsoft Entra ID real ni entrega real de correos.
6. Se **preservaron intactas todas las tarjetas de ADM-02**.
7. Se registró la firma de la sesión en [.local-context/bitacora.md](../../.local-context/bitacora.md).
8. Se actualizaron las fechas límite a hoy (24 de septiembre de 2026) y prioridad Alta en ClickUp para dar seguimiento al trabajo técnico.

---

## 2. Detalle de Payloads y Comentarios Publicados en ClickUp

### A1 — `86e3duybm` (01.4.1.a F5 Login con respaldo por email)
- **ID ClickUp:** [`86e3duybm`](https://app.clickup.com/t/86e3duybm)
- **Nombre:** `F1-ADM-01.4.1.a · F5 Login con respaldo por email y paso a Activo`
- **Estado Resultante:** `revisión - eliam`
- **Prioridad:** Alta (High)
- **Fecha Límite:** 2026-09-24
- **ID Comentario REST API:** `90170255510123`
- **Criterios Cubiertos:** `01-08`, `02-02` (parte local)
- **Payload Publicado:**
```text
@Eliam

Se actualiza el avance de la tarea 86e3duybm en la rama `F1-ADM-01` y se mantiene en estatus pendiente de validación en QA de desarrollo (Puerta A).

Avance completado y subido en la rama `F1-ADM-01`:
• Commits publicados en el repositorio:
  - `3f90cb1`, `951543d`, `4869d68`, `561cfc7`, `aad74ec` (y `8c63a5a`, `e029859`, `f9ca79f` del 23/09)
• Cambios técnicos / Arquitectura / Backend:
  - El usuario con OID `pending:` / `dev-` se vincula al OID real en el primer login conservando `Usuario.Id`, roles y sucursal.
  - El respaldo por correo solo opera con coincidencia controlada.
  - Un correo ya vinculado a otro OID se rechaza (422 `USUARIO_EMAIL_YA_VINCULADO_OTRO_OID`, evita secuestro de cuenta).
• Pruebas / Estabilidad / Evidencia:
  - Documentada evidencia técnica local en `docs/handoff/21-prueba-local-adm01-adm02-2026-09-23.md`.
  - Criterios cubiertos: 01-08, 02-02 (parte local).

Pendiente para la siguiente sesión:
• Login con tenant real Microsoft Entra ID (Puerta B).
```

---

### A2 — `86e3duyd8` (01.4.1.b F6 Ciclo de vida del colaborador)
- **ID ClickUp:** [`86e3duyd8`](https://app.clickup.com/t/86e3duyd8)
- **Nombre:** `F1-ADM-01.4.1.b · F6 Ciclo de vida del colaborador`
- **Estado Resultante:** `revisión - eliam`
- **Prioridad:** Alta (High)
- **Fecha Límite:** 2026-09-24
- **ID Comentario REST API:** `90170255510126`
- **Criterios Cubiertos:** `01-07`, `01-12`
- **Payload Publicado:**
```text
@Eliam

Se actualiza el avance de la tarea 86e3duyd8 en la rama `F1-ADM-01` y se mantiene en estatus pendiente de validación en QA de desarrollo (Puerta A).

Avance completado y subido en la rama `F1-ADM-01`:
• Commits publicados en el repositorio:
  - `3f90cb1`, `951543d`, `4869d68`, `561cfc7`, `aad74ec` (y `8c63a5a`, `e029859`, `f9ca79f` del 23/09)
• Cambios técnicos / Arquitectura / Backend:
  - Sincronización de departamento y puesto en actualización (PATCH).
  - Baja laboral inhabilita el usuario y la provisión pendiente.
  - Recontratación no reactiva el acceso sin decisión explícita.
  - Flujo "Dar acceso" a empleados existentes.
• Pruebas / Estabilidad / Evidencia:
  - Documentada evidencia técnica local en `docs/handoff/21-prueba-local-adm01-adm02-2026-09-23.md`.
  - Criterios cubiertos: 01-07, 01-12.

Pendiente para la siguiente sesión:
• Validación e integración de QA de desarrollo (Puerta A).
```

---

### A3 — `86e3duyem` (01.4.1.c F7 Wizard de alta en 5 pasos + pestaña Acceso)
- **ID ClickUp:** [`86e3duyem`](https://app.clickup.com/t/86e3duyem)
- **Nombre:** `F1-ADM-01.4.1.c · F7 Wizard de alta en 5 pasos + pestaña Acceso`
- **Estado Resultante:** `revisión - eliam`
- **Prioridad:** Alta (High)
- **Fecha Límite:** 2026-09-24
- **ID Comentario REST API:** `90170255510128`
- **Criterios Cubiertos:** `01-07`, `01-08`, `01-09`
- **Payload Publicado:**
```text
@Eliam

Se actualiza el avance de la tarea 86e3duyem en la rama `F1-ADM-01` y se mantiene en estatus pendiente de validación en QA de desarrollo (Puerta A).

Avance completado y subido en la rama `F1-ADM-01`:
• Commits publicados en el repositorio:
  - `3f90cb1`, `951543d`, `4869d68`, `561cfc7`, `aad74ec` (y `8c63a5a`, `e029859`, `f9ca79f` del 23/09)
• Cambios técnicos / UI / Backend:
  - Alta sin acceso, con cuenta existente y con cuenta nueva (directorio simulado + correo capturado en Mailpit).
  - Rol y sucursal persistidos correctamente.
  - Manejo de estados: pendiente, aprovisionada y error con reintento.
• Pruebas / Estabilidad / Evidencia:
  - Documentada evidencia técnica local en `docs/handoff/21-prueba-local-adm01-adm02-2026-09-23.md`.
  - Criterios cubiertos: 01-07, 01-08, 01-09.

Pendiente para la siguiente sesión:
• Entrega real del correo vía tenant Microsoft Entra ID (Puerta B).
```

---

### A4 — `86e3d1kr4` (01.4.2 Master-Detail de Empleado)
- **ID ClickUp:** [`86e3d1kr4`](https://app.clickup.com/t/86e3d1kr4)
- **Nombre:** `F1-ADM-01.4.2 · Vista Master-Detail de Empleado (/admin/empleados/$id) con gestión de roles y sucursales`
- **Estado Resultante:** `revisión - eliam`
- **Prioridad:** Alta (High)
- **Fecha Límite:** 2026-09-24
- **ID Comentario REST API:** `90170255510130`
- **Criterios Cubiertos:** `01-06`, `01-11`
- **Payload Publicado:**
```text
@Eliam

Se actualiza el avance de la tarea 86e3d1kr4 en la rama `F1-ADM-01` y se mantiene en estatus pendiente de validación en QA de desarrollo (Puerta A).

Avance completado y subido en la rama `F1-ADM-01`:
• Commits publicados en el repositorio:
  - `3f90cb1`, `951543d`, `4869d68`, `561cfc7`, `aad74ec` (y `8c63a5a`, `e029859`, `f9ca79f` del 23/09)
• Cambios técnicos / UI / Frontend / Backend:
  - Ficha única de empleado (`/admin/empleados/$id`) con pestañas: Datos generales, Puesto y departamento, Sucursales operativas, Roles y accesos.
  - Sección de Cuentas de acceso distingue adecuadamente "Empleado vinculado" vs "Sin empleado vinculado".
• Pruebas / Estabilidad / Evidencia:
  - Documentada evidencia técnica local en `docs/handoff/21-prueba-local-adm01-adm02-2026-09-23.md`.
  - Criterios cubiertos: 01-06, 01-11.

Pendiente para la siguiente sesión:
• Validación final en recorridos de QA de desarrollo (Puerta A).
```

---

### A5 — `86e3d1krk` (01.4.3 Auditoría del alcance de sucursal)
- **ID ClickUp:** [`86e3d1krk`](https://app.clickup.com/t/86e3d1krk)
- **Nombre:** `F1-ADM-01.4.3 · Registro de auditoría del alcance de sucursal en bitácora (AuditLogEntry)`
- **Estado Resultante:** `revisión - eliam`
- **Prioridad:** Alta (High)
- **Fecha Límite:** 2026-09-24
- **ID Comentario REST API:** `90170255510133`
- **Criterio Cubierto:** `01-14` (parte bitácora)
- **Payload Publicado:**
```text
@Eliam

Se actualiza el avance de la tarea 86e3d1krk en la rama `F1-ADM-01` y se mantiene en estatus pendiente de validación en QA de desarrollo (Puerta A).

Avance completado y subido en la rama `F1-ADM-01`:
• Commits publicados en el repositorio:
  - `3f90cb1`, `951543d`, `4869d68`, `561cfc7`, `aad74ec` (y `8c63a5a`, `e029859`, `f9ca79f` del 23/09)
• Cambios técnicos / Backend / UI:
  - La bitácora guarda sucursal (ID + clave) y el modo de acceso en `AuditLogEntry`.
  - Vista `/admin/auditoria` filtra eventos por sucursal y conserva eventos globales sin filtro.
  - Consultas acotadas a la empresa actual.
• Pruebas / Estabilidad / Evidencia:
  - Documentada evidencia técnica local en `docs/handoff/21-prueba-local-adm01-adm02-2026-09-23.md`.
  - Criterio cubierto: 01-14 (parte bitácora).

Pendiente para la siguiente sesión:
• Pruebas de integración adicionales para escrituras y alcance por sucursal (Parte B).
```

---

### A6 — `86e3b2vjg` (01.5 Sucursal activa)
- **ID ClickUp:** [`86e3b2vjg`](https://app.clickup.com/t/86e3b2vjg)
- **Nombre:** `F1-ADM-01.5 · Contexto de empresa + sucursal activa en Administración`
- **Estado Resultante:** `en progreso`
- **Prioridad:** Alta (High)
- **Fecha Límite:** 2026-09-24
- **ID Comentario REST API:** `90170255510136`
- **Payload Publicado:**
```text
@Eliam

Se actualiza el avance de la tarea 86e3b2vjg en la rama `F1-ADM-01` y se mantiene en estatus en progreso para concluir alcance por sucursal en escrituras y vistas de Administración.

Avance completado y subido en la rama `F1-ADM-01`:
• Commits publicados en el repositorio:
  - `3f90cb1`, `951543d`, `4869d68`, `561cfc7`, `aad74ec` (y `8c63a5a`, `e029859`, `f9ca79f` del 23/09)
• Cambios técnicos / UI / Backend:
  - `SucursalSelector` en sesión (`/api/auth/sucursales`).
  - `currentSucursalId` en el store.
  - Catálogo de empleados filtrado.
  - Permiso `admin.empleados.leer-todas-sucursales` (migración `20260924144727_AddEmpleadosLeerTodasSucursales`).
• Pruebas / Estabilidad / Evidencia:
  - Documentada evidencia técnica local en `docs/handoff/21-prueba-local-adm01-adm02-2026-09-23.md`.

Pendiente para la siguiente sesión:
• Pasos B1 (escrituras backend) y B2 (vistas frontend) de este plan.
```

---

### A7 — `86e3duyhk` (01.4.1.d F8 Reconciliación A–G)
- **ID ClickUp:** [`86e3duyhk`](https://app.clickup.com/t/86e3duyhk)
- **Nombre:** `F1-ADM-01.4.1.d · F8 Reconciliación de datos existentes (casos A–G)`
- **Estado Resultante:** `en espera`
- **Prioridad:** Alta (High)
- **Fecha Límite:** 2026-09-24
- **ID Comentario REST API:** `90170255510139`
- **Payload Publicado:**
```text
@Eliam

Se actualiza el avance de la tarea 86e3duyhk en la rama `F1-ADM-01` y se mantiene en estatus en espera para concluir la corrida y revisión humana de reconciliación.

Avance completado y subido en la rama `F1-ADM-01`:
• Commits publicados en el repositorio:
  - `3f90cb1`, `951543d`, `4869d68`, `561cfc7`, `aad74ec` (y `8c63a5a`, `e029859`, `f9ca79f` del 23/09)
• Cambios técnicos / SQL:
  - Reporte de solo lectura `tools/reporte-reconciliacion-colaboradores.sql`.

Pendiente para la siguiente sesión:
• Ejecutar reporte en la base de QA y realizar la revisión humana de los casos B/E/F/G (paso C3).
```

---

### A8 — `86e3d1kqv` (01.4.1 Alta unificada - padre)
- **ID ClickUp:** [`86e3d1kqv`](https://app.clickup.com/t/86e3d1kqv)
- **Nombre:** `F1-ADM-01.4.1 · Alta unificada de colaborador (Usuario + Empleado, Entra ID)`
- **Estado Resultante:** `en progreso`
- **Prioridad:** Alta (High)
- **Fecha Límite:** 2026-09-24
- **ID Comentario REST API:** `90170255510142`
- **Payload Publicado:**
```text
@Eliam

Se actualiza el avance de la tarea padre 86e3d1kqv en la rama `F1-ADM-01` y se mantiene en estatus en progreso.

Resumen del avance del paquete 01.4.1 (Alta unificada):
• Subtareas 01.4.1.a, 01.4.1.b y 01.4.1.c enviadas a `revisión - eliam` (hecho en local, pendiente Puerta A).
• Subtarea 01.4.1.d (Reconciliación A–G) en estatus `en espera` (reporte de solo lectura `tools/reporte-reconciliacion-colaboradores.sql` listo, pendiente corrida en QA).
• Subtareas 01.4.1.e (Graph real, TI) y 01.4.1.f (ADR-0052, owner) continúan abiertas para Puerta B / decisiones de arquitectura.

Pendiente para la siguiente sesión:
• Concluir pruebas de integración de Puerta A y alineación con owner/TI.
```

---

## 3. Matriz de Tareas de ADM-01 en ClickUp

| Subtarea | Título | Estado Actual | Prioridad | Fecha Límite |
|---|---|---|---|---|
| [`86e3a6c9g`](https://app.clickup.com/t/86e3a6c9g) | **F1-ADM-01 (Principal)** | En progreso | Alta | 2026-09-24 |
| [`86e3b2vhp`](https://app.clickup.com/t/86e3b2vhp) | F1-ADM-01.3 · Administración de sucursales | Revisión - Eliam | Alta | 2026-09-24 |
| [`86e3d1rfq`](https://app.clickup.com/t/86e3d1rfq) | F1-ADM-01.3.1 · Layout Master-Detail sucursales | Revisión - Eliam | Alta | 2026-09-24 |
| [`86e3b2vnq`](https://app.clickup.com/t/86e3b2vnq) | F1-ADM-01.4 · Departamentos, puestos y relaciones | Revisión - Eliam | Alta | 2026-09-24 |
| [`86e3d1kqv`](https://app.clickup.com/t/86e3d1kqv) | **F1-ADM-01.4.1 · Alta unificada (Padre)** | En progreso | Alta | 2026-09-24 |
| [`86e3duybm`](https://app.clickup.com/t/86e3duybm) | 01.4.1.a · F5 Login respaldo email | Revisión - Eliam | Alta | 2026-09-24 |
| [`86e3duyd8`](https://app.clickup.com/t/86e3duyd8) | 01.4.1.b · F6 Ciclo de vida | Revisión - Eliam | Alta | 2026-09-24 |
| [`86e3duyem`](https://app.clickup.com/t/86e3duyem) | 01.4.1.c · F7 Wizard alta + Acceso | Revisión - Eliam | Alta | 2026-09-24 |
| [`86e3duyhk`](https://app.clickup.com/t/86e3duyhk) | 01.4.1.d · F8 Reconciliación A-G | En espera | Alta | 2026-09-24 |
| [`86e3d1kr4`](https://app.clickup.com/t/86e3d1kr4) | 01.4.2 · Master-Detail Empleado | Revisión - Eliam | Alta | 2026-09-24 |
| [`86e3d1krk`](https://app.clickup.com/t/86e3d1krk) | 01.4.3 · Auditoría sucursal | Revisión - Eliam | Alta | 2026-09-24 |
| [`86e3b2vjg`](https://app.clickup.com/t/86e3b2vjg) | 01.5 · Contexto sucursal activa | En progreso | Alta | 2026-09-24 |
| [`86e3b2vmd`](https://app.clickup.com/t/86e3b2vmd) | 01.6 · Pruebas y evidencia final | En espera | Alta | 2026-09-24 |

---

## 4. Firma de Bitácora

Registrada en [.local-context/bitacora.md](../../.local-context/bitacora.md):

```markdown
#### [2026-09-24 16:50] - Antigravity (Gemini 3.8 Flash, supervisor documentador)
- **Tarea ClickUp:** Subtareas de `86e3a6c9g` (F1-ADM-01): `86e3duybm`, `86e3duyd8`, `86e3duyem`, `86e3d1kr4`, `86e3d1krk`, `86e3b2vjg`, `86e3duyhk`, `86e3d1kqv`. https://app.clickup.com/t/86e3a6c9g
- **Archivos Modificados:** `.local-context/bitacora.md`
- **Resumen Técnico:**
  - Ejecución completa de la **Parte A (A1–A11)** del plan activo (`.local-context/plan-activo.md`).
  - Actualización de estatus y publicación de avance intermedio (**Modalidad A**, mencionando únicamente a `@Eliam`) en ClickUp mediante API REST v2.
- **Bloqueos/Pendientes:** Parte A finalizada al 100%. Continuar con la Parte B (B1–B7) para el cierre de huecos de código de ADM-01.
```
