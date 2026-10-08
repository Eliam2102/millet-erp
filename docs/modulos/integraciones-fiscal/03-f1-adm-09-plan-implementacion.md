# F1-ADM-09 · Plan de implementación actualizado

**Actualizado:** 2026-09-29

**Rama:** `feature/F1-ADM-09`

**Responsable:** Uziel

**Estado:** implementación parcial; cierre completo pendiente

**Fuente funcional:** ficha ADM-09 ajustada el 28-sep-2026

## 1. Resultado contractual

Configuración administrativa completa del emisor fiscal, PAC, CSD y series, con permisos explícitos, aislamiento por empresa y sucursal autorizada, separación por tipo de comprobante, reserva concurrente sin duplicados, estado visible del CSD, mensajes claros y evidencia sin secretos.

ADM-09 no reconstruye el motor de timbrado, cancelación, descarga ni complementos. Esas capacidades consumen esta configuración.

## 2. Estado después de `4103239`

| Requisito | Estado | Brecha |
|---|---|---|
| Permiso PAC/CSD | Cubierto | API/UI usan `integraciones.fiscal.administrar` |
| PAC aislado por empresa | Cubierto | `EmpresaId`, guard HTTP y pruebas cross-tenant |
| Secretos cifrados/no expuestos | Cubierto | Data Protection, DTO enmascarado y regresiones |
| Emisor fiscal | Parcial | Empresa/RFC/régimen/CP existen; falta recorrido ADM-09 |
| Series por sucursal | Parcial | `SucursalId` existe; falta autorización integral y filtrado |
| Series por tipo | Parcial | Modelo/reserva lo soportan; falta evidencia negativa explícita |
| Concurrencia | Preexistente | UPSERT + índice + prueba de 50; falta ejecutarla/documentarla |
| Reserva sin permiso | Faltante | Endpoint público sólo exige autenticación |
| Serie desactivada | Parcial | Falta desactivar → no reservar + histórico consultable |
| Estado del CSD | Parcial | Se rechaza vencido; UI no muestra vigente/próximo/vencido |
| Configuración incompleta/PAC caído | Parcial | Errores puntuales sin cierre UI/API completo |
| Sandbox real | Pendiente externo | Sin ApiKey/CSD sandbox entregados por canal seguro |
| PR integrado en `main` | Pendiente | Rama remota creada, cierre completo no integrado |

## 3. Reglas de diseño

1. Reutilizar `Empresa`, `Serie`, `SecuenciaFolio`, permisos, `UsuarioSucursal` y FiscalAPI existentes.
2. No crear otra entidad de series, contador o PAC.
3. `SucursalId` enviado por el cliente no autoriza: validar asignaciones reales del usuario.
4. Definir explícitamente cuándo una serie global (`SucursalId = null`) puede usarse.
5. Factura/Ingreso y Nota de crédito/Egreso conservan secuencias independientes.
6. Secretos y ciphertext nunca regresan en DTO, logs o evidencia.
7. `NotBefore`/`NotAfter` del certificado son metadatos no secretos.
8. Un doble del PAC prueba manejo de errores, nunca prueba sandbox real.

## 4. Fases

### Fase 1 · Permisos y sucursal

- Resolver sucursales autorizadas reutilizando `UsuarioSucursal`.
- Aplicar alcance a listado, detalle, alta, edición, desactivación y reserva.
- Añadir filtro opcional y columna de sucursal a Series.
- Proteger la reserva HTTP con el permiso correspondiente; conservar usos internos por MediatR.
- Mantener empresa como primera barrera.

**Salida:** usuarios de sucursales distintas no ven, alteran ni reservan la serie ajena.

### Fase 2 · Tipo, desactivación y concurrencia

- Probar secuencias independientes de Factura/Ingreso y Nota de crédito/Egreso.
- Ejecutar la prueba existente de 50 reservas concurrentes únicas.
- Probar desactivar → reserva rechazada y consulta histórica disponible.
- No sustituir el UPSERT ni el índice actuales.

**Salida:** separación y concurrencia demostradas reproduciblemente.

### Fase 3 · Emisor y estado del CSD

- Reutilizar Empresa para RFC, razón social, régimen y código postal.
- Validar permisos y recorrido UI/API; no crear otra entidad de emisor.
- Extraer y persistir sólo `NotBefore`/`NotAfter` durante la validación del CSD.
- Derivar estado con umbral documentado de 30 días: `Vigente`, `ProximoAVencer`, `Vencido`.
- Exponer estado/expiración y mostrar badge, sin material criptográfico.
- Crear migración únicamente para estos metadatos.

**Salida:** configuración del emisor y vigencia del CSD visibles de forma segura.

### Fase 4 · Configuración incompleta y PAC

- Reutilizar códigos existentes de PAC/CSD/series.
- Mostrar mensajes accionables para emisor incompleto, CSD ausente/vencido, serie inexistente/inactiva y PAC no disponible.
- Cubrir éxito, timeout y 503 con doble determinista.
- Verificar que `Probar conexión` no muta configuración y un rechazo conocido no deja estado ambiguo.
- No rediseñar la reconciliación de timbrados ambiguos.

**Salida:** errores claros y verificables en API/UI.

### Fase 5 · Evidencia y cierre

- Ejecutar suites focalizadas y regresión proporcional.
- Recorrer UI con usuario autorizado/no autorizado, dos sucursales y dos tipos.
- Revisar Network, consola y logs para ausencia de secretos.
- Probar `https://test.fiscalapi.com` sólo con ApiKey/CSD sandbox válidos.
- Sin insumos, registrar bloqueo externo; nunca simular éxito.
- Crear PR ADM-09 e integrar después de todas las puertas técnicas.

## 5. Commits propuestos

| # | Commit | Alcance |
|---:|---|---|
| 1 | `fix(admin): aplicar alcance de sucursal a series` | Listado, mutaciones, reserva y UI |
| 2 | `test(admin): acreditar series fiscales y concurrencia` | Tipo, desactivación y simultaneidad |
| 3 | `feat(fiscal): exponer estado seguro del CSD` | Metadatos, migración, DTO y badge |
| 4 | `fix(fiscal): aclarar configuracion incompleta y PAC caido` | Mensajes y pruebas negativas |
| 5 | `docs(fiscal): cerrar evidencia ADM-09` | Automatización, UI y sandbox |

No mezclar refactors generales ni regenerar `routeTree.gen.ts` sin cambio de ruta fuente.

## 6. Archivos probables

### Series

- `backend/src/Compartido/Application/Administracion/Series/**`: alcance en queries/comandos.
- `backend/src/Api/Endpoints/Administracion/SeriesEndpoints.cs`: permiso y filtro.
- `backend/tests/Api.IntegrationTests/Administracion/SeriesEndpointsTests.cs`: sucursales, tipos, desactivación y concurrencia.
- Componentes/API existentes de Series en frontend: filtro/columna de sucursal y mensajes.

### Emisor y CSD

- Endpoint/formulario de Empresa existentes: pruebas y cambio sólo ante brecha demostrada.
- `backend/src/Integraciones.Fiscal/Domain/ConfiguracionPac.cs`: metadatos de vigencia.
- `backend/src/Integraciones.Fiscal/Infrastructure/Cifrado/CsdValidador.cs`: fechas validadas.
- `backend/src/Integraciones.Fiscal/Application/Configuracion/**`: persistencia/proyección.
- Persistencia, migración y snapshot de Integraciones Fiscal.
- `frontend/src/features/integraciones-fiscal/**`: tipos, badge y mensajes.

### Documentación

- Este plan y `docs/handoff/30-evidencia-f1-adm-09.md`.

Resolver rutas exactas con `rg` antes de editar; no crear duplicados.

## 7. Pruebas automatizadas obligatorias

### Autorización

- 401 sin autenticar.
- 403 sin permiso para PAC/emisor/series/reserva HTTP.
- Empresa A no opera B.
- Usuario de sucursal A no lista, modifica ni reserva B.
- Serie global respeta política explícita.

### Series

- Factura y Nota de crédito mantienen secuencias independientes.
- 50 reservas concurrentes producen 50 folios únicos.
- Serie desactivada rechaza reserva; histórico sigue consultable.

### CSD/secretos

- Límites vigente, próximo a vencer (≤30 días) y vencido.
- Rotación actualiza fechas; eliminación las limpia.
- DTO/JSON no contiene ApiKey, certificado, llave, password, hash o ciphertext.
- Lector ve estado pero no muta.

### PAC

- Éxito, timeout y 503 normalizados.
- Emisor/CSD/serie faltantes generan mensaje accionable.
- Prueba de conexión no muta ni deja estado ambiguo.

```powershell
dotnet test backend/tests/Integraciones.Fiscal.UnitTests/Millet.Integraciones.Fiscal.UnitTests.csproj --no-restore
dotnet test backend/tests/Api.IntegrationTests/Millet.Api.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~IntegracionesFiscal|FullyQualifiedName~SeriesEndpointsTests"
npm --prefix frontend run test -- ConfiguracionPacForm
npm --prefix frontend run typecheck
npm --prefix frontend run lint
```

Un runner bloqueado se reporta como no concluyente, nunca como verde.

## 8. Verificación UI

1. Sin permisos: no editar emisor/PAC/CSD/series ni reservar por API pública.
2. Administrador: configurar emisor reutilizando Empresa y guardar PAC/CSD sandbox.
3. Recargar: secretos no reaparecen; sólo estado y expiración.
4. Validar badges vigente, próximo a vencer y vencido.
5. Usuario sucursal A sólo ve/reserva series permitidas.
6. Sucursal B no puede usar A por UI ni URL/API directa.
7. Factura y Nota de crédito reservan contadores independientes.
8. Serie desactivada bloquea reserva; histórico continúa visible.
9. Timeout/503 del PAC muestra mensaje claro sin secretos ni estado indeterminado.
10. Revisar Network, consola y logs.
11. Con insumos autorizados, ejecutar una prueba real sandbox y registrar sólo resultado, timestamp, duración y correlation id no sensible.

## 9. Definition of Done

- Todos los criterios ADM-09 cubiertos o declarados dependencia externa.
- Alcance por sucursal validado contra asignaciones reales.
- Permisos aplicados a configuración y reserva pública.
- Tipo, desactivación y concurrencia demostrados.
- Emisor reutilizado y CSD con estado seguro visible.
- Mensajes de configuración incompleta/PAC caído verificados.
- Ningún secreto expuesto.
- Evidencia automática/UI actualizada.
- PR propio integrado en `main`; sandbox real sólo puede quedar bloqueado por insumos externos documentados.
