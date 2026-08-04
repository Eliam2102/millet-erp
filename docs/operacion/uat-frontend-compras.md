# Plan de UAT del frontend — Compras Requisiciones

> Plan específico del **frontend** del módulo Compras Requisiciones.
> Complementa [plan-uat-compras.md](plan-uat-compras.md) (que cubre
> el flujo end-to-end backend + frontend) con foco en la **UX**, los
> **estados de UI**, accesibilidad y perf perceptual desde la pantalla.

## 1. Alcance

Validar que las pantallas implementadas en UF1-PR2 → UF7-PR3 cubren
la operación diaria del autorizador, capturador y administrador de
aprobadores, con UX consistente y sin regresiones funcionales sobre
el backend.

**Pantallas en alcance:**
- P1 — Bandeja general de Requisiciones (`/compras/requisiciones`)
- P2 — Bandeja de pendientes de autorización (`/compras/pendientes`)
- P3 — Detalle de requisición (`/compras/requisiciones/$id`)
- P4 — Nueva requisición (`/compras/requisiciones/nueva`)
- P5 — Editor de líneas (dentro de P3 con permisos)
- P9 — Admin de aprobadores (`/compras/admin/aprobadores`)
- P11 — Ayuda (`/compras/ayuda`)

**Fuera de alcance** (van en UAT separado):
- Mobile experience (UF7-PR3 follow-up).
- Performance benchmark con seed 10k (UF7-PR3 follow-up).
- E2E con Playwright contra backend real (UF7-PR2).

## 2. Grupo piloto

| Rol UAT | Permisos típicos | N° de usuarios | Responsable |
|---|---|---|---|
| Capturador (depto Mantenimiento) | `requisiciones.crear`, `requisiciones.editar`, `requisiciones.leer` | 2 | TBD owner |
| Autorizador N1 (jefe depto) | `requisiciones.autorizar-nivel1`, `requisiciones.leer` | 2 | TBD owner |
| Autorizador N2 (gerencia) | `requisiciones.autorizar-nivel2`, `requisiciones.rechazar`, `requisiciones.cancelar`, `requisiciones.leer` | 1 | TBD owner |
| Admin aprobadores | `aprobadores.administrar` (no requiere los demás) | 1 | TBD owner |
| Lectura cross-depto | `requisiciones.leer` + `requisiciones.ver-todos-departamentos` | 1 | TBD owner |

## 3. Flujos a validar

### F1 — Capturador captura y transmite (P4 + P3)
1. Login con usuario capturador.
2. Sidebar → Compras → modal App Launcher → "Mis requisiciones".
3. Click "Nueva requisición".
4. Llenar cabecera (sucursal / depto / almacén destino /
   clasificación / prioridad).
5. Submit → redirige al detalle con estado **Borrador**.
6. Agregar 3 líneas (artículos del catálogo + cantidad + precio).
7. Editar una línea (cambiar cantidad).
8. Eliminar una línea.
9. Click "Transmitir" → confirm → estado pasa a **EnAutorización**.
10. **Verificar**: la RQ aparece en bandeja P1 con estado correcto;
    los botones de acciones cambian (ya no se puede editar líneas).

### F2 — Autorizador firma N1 (P2 + P3)
1. Login con usuario N1.
2. Sidebar → Compras → "Pendientes de autorización".
3. Verificar que solo ve RQs en estado **EnAutorización** que
   requieren su firma.
4. Click "Ver" → detalle.
5. Click "Aprobar Nivel 1" → confirm → toast de éxito.
6. **Verificar**: la RQ ya no aparece en su pendientes; aparece
   en P1 con estado actualizado.

### F3 — Autorizador rechaza (P3 + ModalMotivo)
1. Login con autorizador con permiso de rechazar.
2. Abrir una RQ EnAutorización.
3. Click "Rechazar" → modal de motivo.
4. Seleccionar motivo "OTRO" → la textarea pasa a "(requerido)".
5. Tipear texto → click "Confirmar rechazo".
6. **Verificar**: estado **Rechazada**, motivo + texto visible en
   la cabecera, ya no aparece en pendientes.

### F4 — Capturador elimina pre-aut (P3 + ModalMotivo)
1. Login con capturador.
2. Abrir una RQ Borrador propia.
3. Click "Eliminar" → modal de motivo (variante eliminar, rojo).
4. Seleccionar motivo → confirmar.
5. **Verificar**: estado **Eliminada**, no aparece en bandejas
   normales.

### F5 — Cancelar post-aut (P3)
1. Login con usuario con permiso `cancelar`.
2. Abrir RQ **Autorizada** o **EnSurtido**.
3. Click "Cancelar" → modal de motivo (variante cancelar, ámbar).
4. Seleccionar motivo → confirmar.
5. **Verificar**: estado **Cancelada**, toast con descripción
   "Reservas liberadas...". (Si el backend devuelve 422
   `CANCELAR_FALLO`, se muestra toast con traceId + CTA Reintentar.)

### F6 — Admin de aprobadores (P9)
1. Login con admin.
2. Sidebar → Compras → "Aprobadores" (sección Configuración del modal).
3. Tab Vigentes → click "Designar aprobador".
4. Seleccionar departamento, rol, usuario → confirmar.
5. **Verificar**: la designación aparece en la tabla.
6. Click ícono basurero en una fila → confirmar revocación.
7. Tab "Histórico" → seleccionar departamento como filtro.
8. **Verificar**: aparece la designación cerrada con su
   `vigenteHasta`.

### F7 — Conflict resolution (UF3-PR3)
> Requiere dos sesiones simultáneas (dos browsers/incognito).
1. Sesión A abre RQ X y empieza a transmitir.
2. Sesión B la rechaza primero.
3. Sesión A intenta transmitir → ve dialog "Esta requisición fue
   actualizada por otro usuario" con `traceId`.
4. Click "Refrescar y revisar" → la RQ se actualiza al estado
   actual (Rechazada).

### F8 — Ayuda (P11)
1. Click ícono "?" en topbar.
2. **Verificar**: la página `/compras/ayuda` carga con las 6
   secciones (estados, ciclo de vida, naturalezas, matriz,
   conceptos, FAQ).
3. Verificar que los EstadoBadge tienen contraste suficiente
   (ojo del usuario o herramienta de browser).
4. "Volver a la bandeja" navega a P1.

### F9 — Impresión (Ctrl+P en P3)
1. Abrir P3 detalle de una RQ.
2. Ctrl+P (o Cmd+P en Mac) → preview del navegador.
3. **Verificar**: el sidebar y el topbar NO aparecen en la
   impresión, los botones de acciones tampoco; las tablas tienen
   bordes sólidos legibles; los badges muestran texto sin fondo de
   color.

## 4. Criterios de aceptación de UI

- [ ] Cada pantalla carga sin errores en consola del navegador.
- [ ] Estados loading muestran skeleton (no spinner aislado).
- [ ] Estados empty tienen mensaje claro y CTA cuando aplica
  (ej. "Crear nueva requisición").
- [ ] Estados error muestran `ErrorState` con título +
  `traceId` visible para soporte.
- [ ] Permisos: usuarios sin permiso NO ven los botones / cards /
  ítems de menú correspondientes.
- [ ] Toasts de éxito y error se cierran solos en 5s; los de error
  con `CANCELAR_FALLO` (15s) permiten click manual.
- [ ] Conflict dialog se abre cuando dos sesiones colisionan; el
  modo simple es claro (botón único "Refrescar y revisar").
- [ ] Ayuda accesible desde topbar en cualquier pantalla del
  módulo.
- [ ] Impresión limpia en A4.

## 5. Métricas de éxito

| Métrica | Target | Cómo se mide |
|---|---|---|
| Errores de consola | 0 críticos | DevTools del navegador en cada flujo |
| Tiempo a interactivo de P1 (red local) | < 1s | DevTools Performance recording |
| % de usuarios piloto que completan F1-F8 sin asistencia | > 80% | Observación en sesión UAT |
| Bugs P0/P1 detectados | 0 P0 antes de release | Issue tracker tagged "uat-frontend" |

## 6. Procedimiento

1. **Pre-UAT** (1 día):
   - Backend en ambiente UAT con seed mínimo (10 RQs en distintos
     estados, 5 usuarios con permisos correspondientes, catálogo
     de artículos básicos).
   - Frontend desplegado contra el backend UAT.
   - Reunión de kickoff con el grupo piloto: explicar alcance y
     entregar este plan.
2. **UAT** (3-5 días):
   - Cada usuario corre los flujos F1-F9 que aplican a su rol y
     reporta:
     - ✅ Pass / ❌ Fail / ⚠ Observación.
     - Para Fail: pasos a reproducir + screenshot + número del
       traceId si hay error.
3. **Triage diario** (15min):
   - Owner del frontend revisa los reports.
   - P0/P1 entran a backlog inmediato; P2 se difieren a
     post-release con acuerdo del owner.
4. **Sign-off**:
   - Owner del proyecto firma cuando F1-F9 están en ✅ y los P0/P1
     están cerrados.

## 7. Riesgos conocidos al entrar a UAT

- **Mobile no se valida** en este UAT (se hará en uno separado
  cuando UF7-PR3 follow-up entregue `<MobileSidebar>`). Documentar
  en plan general.
- **Performance con seeds reales** se valida en `plan-uat-compras.md`
  (UAT general) — éste solo cubre datos sintéticos.
- **Conflict dialog modo preserve** no está implementado en
  UF3-PR3 (solo simple). Si el grupo piloto reporta pérdida de
  trabajo en escenarios complejos, se acelera el follow-up.

## 8. Anexos

- Convenciones operativas: [frontend/docs/patrones-compras.md](../../frontend/docs/patrones-compras.md)
- ADR-0032 shell de navegación: [docs/decisiones/0032-shell-de-navegacion-app-launcher.md](../decisiones/0032-shell-de-navegacion-app-launcher.md)
- Plan UAT general (backend + frontend integrado): [plan-uat-compras.md](plan-uat-compras.md)
