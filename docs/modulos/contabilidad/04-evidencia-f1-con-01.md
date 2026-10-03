# 04 — Evidencia de F1-CON-01a (backend) y datos pendientes de Contabilidad

> Alcance: 01a (F0–F6 + backend de §20). La UI y su evidencia (capturas, Vitest) son 01b. Solo estructura y estadísticas; sin contenido de cuentas reales.

## 1. Resultados de pruebas (ejecutadas 2026-10-02 contra Postgres real, `millet_dev`)

| Suite | Esperado | Obtenido |
|---|---|---|
| `Millet.Contabilidad.UnitTests` (formato×3, normalizador, hoja base, control, R8, perfilado, dominio, opciones) | 69 verdes | **69 passed, 0 failed** |
| `Api.IntegrationTests.Contabilidad` (HTTP real + Postgres real) | 36 verdes | **36 passed, 0 failed** |
| `Api.IntegrationTests.CentrosCosto` (regresión) | verdes | **27 passed** (63 = 36 + 27 en la corrida conjunta) |
| `Identidad.UnitTests` / `Administracion.UnitTests` / `SharedKernel.UnitTests` | verdes | 101 / 130 / 195 passed |
| Build de `Millet.sln` | sin errores | Build succeeded |

Tiempos medidos con 5 000 filas (HTTP): vista previa 0.4 s, perfilado 0.4 s, aplicar 13.1 s. 5 001 filas ⇒ 422 `CONTAB_IMPORT_LIMITE_FILAS`.

Migraciones: `ContabilidadCatalogoInicial` (20261002002340) y `SeedPermisosContabilidadCatalogo` (20261002002434, posterior a 20261001183414) aplicadas; presentes en `public."__EFMigrationsHistory"`. Down de Contabilidad verificado (0 tablas) y re-aplicado.
El Down de la migración de permisos de Identidad falla mientras existan filas en `rol_permisos` que referencian los permisos (el super admin los recibe al arrancar): mismo comportamiento que las demás migraciones de seed de permisos.

## 2. Matriz de aceptación (§12) → prueba

| Criterio | Prueba |
|---|---|
| Importar 1 título + 2 afectables | `Importar_1_titulo_2_afectables_crea_3_cuentas_con_origen_niveles_y_un_lote` |
| Reimportar no duplica / SinCambios | `Reimportar_el_mismo_archivo_no_duplica_…`; unit `Reimportar_el_mismo_archivo_da_SinCambios…` (×3 formatos) |
| Rechazar ciclo / código repetido | `Jerarquia_ciclica_por_PUT…`, `Jerarquia_ciclica_y_codigo_repetido_en_el_archivo…`, `Codigo_repetido_da_409…`, `Carrera_de_dos_altas…` |
| Consumidor acepta/rechaza (activa, afectable, título, inactiva, inexistente, otra empresa, pendiente, control) | `PuertoLecturaTests` (3 pruebas) |
| Cambio incompatible sobre cuenta usada bloqueado sin alterar histórico | `Cambio_incompatible_sobre_cuenta_usada…` (versión, `updated_at`, auditoría y uso intactos) |
| Usuario de consulta no modifica | `Usuario_de_consulta_lee_pero_no_modifica…`, `Administrar_no_implica_importar` |
| Concurrencia / Idempotency-Key | `Concurrencia_If_Match…`, `Idempotency_Key_repetida…` |
| Baja lógica no destruye | `Baja_logica_no_destruye…`, `Codigo_de_cuenta_inactiva_no_se_reutiliza` |
| Aislamiento por empresa | `Otra_empresa_no_ve_ni_modifica…`, `EmpresaId_enviado_en_el_body_se_ignora…` |
| Transaccionalidad | `Archivo_con_errores_es_422…`, `Fallo_a_mitad_de_la_aplicacion_revierte_todo…`, `Dos_importaciones_simultaneas…` |
| Vista previa/perfilado no escriben | `Vista_previa_y_perfilado_no_escriben_ninguna_tabla` |
| §20: 3 formatos de código, sucios, volumen, config al arranque | `ImportadorFormatosTests`, `NormalizadorTests`, `Volumen_5000…`, `Configuracion_inconsistente_tumba_el_arranque…` |
| Permisos canónicos | `Permisos_canonicos_sembrados…` + espejo en `permission-codes.ts` y su test (Vitest NO ejecutado: el worktree no tiene `node_modules`) |

## 3. Puntos provisionales o por defecto (fáciles de revertir)

| # | Qué quedó provisional | Dónde | Cómo revertir / cambiar |
|---|---|---|---|
| 1 | Hoja «Plan de cuentas-VILO» como base; mapeo por alias (Numero→codigo, Cuenta→nombre, agrupador SAT) | `CatalogoOpciones.ColumnasDefault`, `Importacion:Columnas` | Configuración |
| 2 | `Tipo` y `Nivel de cuenta SAT` ignorados con advertencia; columna canónica de título/afectable renombrada a `tipo_cuenta` (difiere del `tipo` de la muestra del plan §6.2) | `ColumnasSinMapeo`, `ColumnasDefault["tipo_cuenta"]` | Configuración (vaciar `ColumnasSinMapeo` y añadir alias) |
| 3 | `naturaleza` y `tipo` anulables; pendiente = nulo en cualquiera | `CuentaContable`, columnas `smallint NULL` | Migración de endurecimiento cuando Contabilidad entregue |
| 4 | `PendienteValidacion` como motivo del puerto (inferencia mía) | `CuentaContableReadAdapter` | Un `if` |
| 5 | Jerarquía por segmentos; segmentos no numéricos = etiquetas; ancho preservado al poner ceros (⇒ `…005` deduce `…000`) | `FormatoCatalogo.PadrePorSegmentos`, `Jerarquia:Modo` | Configuración / un método |
| 6 | Código con patrón permisivo `^[A-Z0-9][A-Z0-9.-]*$`, 1–30; sin relleno de ceros | `CatalogoOpciones.AplicarDefaults` | Configuración |
| 7 | Herencia de naturaleza apagada (R5); `NivelMaximo` 10 | `HerenciaNaturaleza` | Configuración |
| 8 | Cuentas de control: lista vacía; orígenes permitidos AuxiliarCxC→Clientes, AuxiliarCxP→Proveedores | `CuentasControl`, `OrigenesControl` | Configuración |
| 9 | «No afectable por asiento manual» NO implementada (P17 abierta) | — | Añadir booleano si se confirma |
| 10 | Código inmutable por edición e importación (origen con código distinto ⇒ error `CONTAB_IMPORT_ORIGEN_CODIGO_DISTINTO`) | handlers/`ImportadorCatalogo` | Decisión de producto |
| 11 | Celda vacía en importación conserva el valor existente (no se puede vaciar por importación) | `ImportadorCatalogo` | Cambiar la regla de fusión |
| 12 | `POST /cuentas` acepta cualquier padre válido (no obliga a coincidir con el deducido por segmentos) | `CrearCuentaHandler` | Validar contra `PadrePorSegmentos` |
| 13 | Con `CuentasControl` no vacía, un código no listado marcado como control en el archivo es conflicto | `ImportadorCatalogo` | Quitar la rama |
| 14 | Búsqueda: `ILIKE` en código y `translate()` (ADR-0045) en nombre | `ListarCuentasHandler` | — |
| 15 | Sin outbox, sin cableado de consumidores (P13), sin procedimiento de reclasificación | `PLATFORM-TODO` `<OutboxContabilidad>`, `<ContabilidadCuentasConsumidores>`, `<ContabilidadReclasificacion>`, `<ContabilidadAsientos>` | Tareas futuras |
| 16 | Permisos sembrados sin asignar a ningún rol (solo super admin); roles pendientes (P7) | migración de Identidad | Asignar cuando el dueño defina |
| 17 | Helpers de ETag/paginación reutilizados de `CentrosCostoCatalogoEndpoints` (misma assembly, sin modificarlo) | `ContabilidadCatalogoEndpoints` | Extraer a un helper común |

## 4. Datos pendientes de Contabilidad (texto listo para enviar al área)

Formato general de entrega para todo lo que sigue: **texto plano (CSV UTF-8 o hoja de Excel), códigos como TEXTO, sin saldos**. Mientras no lleguen, el sistema funciona con la hoja «Plan de cuentas-VILO» como base provisional y las cuentas quedan «pendientes de validación».

1. **Confirmación de la hoja y estructura final de códigos.**
   - *Qué se necesita:* indicar cuál de las tres hojas del archivo (Plan de cuentas (5), Catalogo, Plan de cuentas-VILO) es la oficial y cuál es la estructura final del código (segmentos y anchos).
   - *Por qué:* hoy se usa VILO por ser la más completa; si la oficial es otra, cambian el patrón del código y la deducción del padre.
   - *Formato:* mensaje + el archivo vigente.
   - *Qué cambia al llegar:* configuración (`Codigo.Patron`, `RellenoCeros`, `Jerarquia.Modo`); código solo si la estructura no cabe en segmentos.
2. **Significado de la columna `Tipo`.**
   - *Necesario:* qué representa (14 categorías, 4 filas vacías) y si equivale a título/afectable, naturaleza u otra clasificación.
   - *Por qué:* hoy se ignora con advertencia para no suponer; sin ella no se puede completar ni la afectabilidad ni la naturaleza.
   - *Formato:* tabla categoría → significado.
   - *Cambia:* configuración (alias y mapeo); un campo nuevo si es una clasificación propia.
3. **Validez de los códigos que rompen el patrón.** Doce códigos tienen 13 caracteres (último segmento de 3 dígitos) frente a 12 de los demás (999.99.99.99). **Actualización 2026-10-02:** el TL identificó que son cuentas de depreciación acumulada con un cero adicional; el importador los resuelve (1 raíz y 11 con padre existente, ninguna huérfana). Propuesta pendiente de Contabilidad: homologarlos a 4 segmentos en una copia de trabajo, conservando el código original en `codigo_origen` (0 colisiones); si se deben conservar los originales, usar `codigo_padre` explícito.
   - *Necesario:* confirmar si son válidos o errores de captura, y a qué cuenta padre pertenecen.
   - *Por qué:* hoy la deducción por segmentos los deja huérfanos (error en la importación).
   - *Formato:* lista de códigos con su padre.
   - *Cambia:* configuración (patrón/ancho) o corrección del archivo.
4. **Naturaleza, afectabilidad (título/afectable) y cuentas de control (clientes/proveedores).**
   - *Necesario:* por cuenta, naturaleza (deudora/acreedora) y si es título o afectable; lista de las cuentas globalizadoras de clientes y de proveedores; si hay una por moneda o por sucursal.
   - *Por qué:* sin estos datos ninguna cuenta puede recibir movimientos (el puerto responde «pendiente de validación»); las de control solo se afectan por su auxiliar.
   - *Formato:* columnas `codigo`, `naturaleza`, `tipo_cuenta` y lista de control (`codigo`, Clientes/Proveedores).
   - *Cambia:* se importan con la misma pantalla (sin código); la lista de control va en configuración. Si hay herencia de naturaleza o más tipos de control, se reabre la regla (código).
5. **Casilla «no afectable por asiento manual» (P17).**
   - *Necesario:* confirmar si existe como atributo propio además de «cuenta de control», y a qué cuentas aplica.
   - *Por qué:* no se implementó para no suponerla.
   - *Formato:* sí/no + lista de cuentas.
   - *Cambia:* un campo y una regla nuevos (código y migración).
6. **Agrupaciones de reporte.**
   - *Necesario:* qué agrupaciones necesitan los reportes (código agrupador SAT, grupo de estado financiero, rubro) y si son de uno o varios niveles.
   - *Por qué:* hoy hay dos campos de texto libre sin catálogo.
   - *Formato:* tabla de agrupaciones y su relación con las cuentas.
   - *Cambia:* si son multinivel, tabla nueva (código); si no, solo datos.
7. **Equivalencias SAP↔ERP.**
   - *Necesario:* si se requieren (Contabilidad indicó que no quiere usar el catálogo actual de SAP) o solo para saldos iniciales.
   - *Por qué:* el modelo ya guarda fuente + código de origen, pero la carga de saldos y el criterio «mapeo documentado» dependen de ello.
   - *Formato:* tabla código SAP → código nuevo.
   - *Cambia:* datos (la correspondencia ya está soportada); si no se requiere, queda sin uso.
8. **Perfiles de consulta/importación y roles.**
   - *Necesario:* qué rol de Millet recibe `contabilidad.catalogo.leer`, `.administrar` e `.importar`; entendido que ejecuta el Contador General y autoriza el Director DAF.
   - *Por qué:* hoy los permisos existen pero solo los tiene el super admin.
   - *Formato:* tabla rol → permisos.
   - *Cambia:* asignación de roles (datos); un flujo de autorización de dos pasos sería código nuevo.
9. **Interpretación del criterio «mapeo documentado».**
   - *Necesario:* confirmar si basta documentar el formato del mapeo con una muestra ficticia (hecho) o se entiende como el mapeo real.
   - *Por qué:* si es el real, el criterio queda abierto hasta recibir los puntos 1–7.
   - *Formato:* respuesta del TL.
   - *Cambia:* solo el estado del criterio.

Además: cargar datos definitivos en un ambiente real requiere aprobación de Contabilidad/Guillermo; el archivo real no se versiona (solo estructura y estadísticas, §20.9).

---

## 01b — UI (frontend)

> Solo datos ficticios `FIX-*`. No se leyeron ni usaron archivos reales de Contabilidad. Sin cambios de backend ni de API.

### 1. Archivos

Creados en `frontend/src/features/contabilidad/`:
`api/types.ts`, `api/hooks.ts` (React Query: árbol perezoso, lista, detalle con ETag, ancestros, crear/editar/baja/reactivar, perfilado, vista previa, aplicar), `lib/errores.ts` (onError unificado), `lib/archivo.ts` (lectura de .csv/.xlsx, reporte de perfilado sin contenido), `schemas/cuenta.ts`, `components/` (`InsigniasCuenta`, `ArbolCuentas`, `CuentaForm`, `NuevaCuentaSheet`, `ConfirmarEstatusCuenta`, `PerfilReporte`, `VistaPreviaTabla`), `pages/` (`CatalogoPage`, `CuentaDetallePage`, `ImportacionPage`) y las pruebas `*.test.ts(x)`.
Rutas (`frontend/src/routes/_app/contabilidad/`): `catalogo.index.tsx` (`/contabilidad/catalogo`), `catalogo.$id.tsx`, `importacion.tsx`; `routeTree.gen.ts` regenerado por TanStack Router (no editado a mano).
Modificados: `frontend/src/lib/nav.ts` (el placeholder «Contabilidad» pasa a módulo con 2 cards gateadas por `leer` / `importar`) y `frontend/src/lib/nav.test.ts` (Contabilidad ya no es «disabled»). El espejo de permisos de `permission-codes.ts` se reutiliza tal cual.

### 2. Componentes reutilizados (y por qué no se creó uno nuevo)

| Reutilizado | Para qué |
|---|---|
| `ui/sheet`, `ui/alert-dialog`, `ui/alert`, `ui/badge`, `ui/button`, `ui/input`, `ui/skeleton`, sonner (`toast`) | Sheet «Nueva cuenta», confirmaciones, avisos, insignias, estados de carga y toasts: no se añadió ningún componente de UI base ni color nuevo; los colores salen de los tokens del design system (ver «Design system» abajo) |
| `ConflictDialogProvider` / `useConflictDialog` (`openSimple`) | Diálogo de recarga ante 409/428 (mismo que Compras y CeCo) |
| `centros-costo/components/internal/Field` | Etiqueta + error de campo del formulario |
| `lib/api` (`apiRequest`, `esConflictoConcurrencia`, `esPrecondicionRequerida`, `applyServerErrors`), patrón `useDetalleCatalogo`/`useMutacionConIfMatch` | If-Match/ETag e Idempotency-Key como los otros módulos |
| `useDebouncedValue` (200 ms), `useHasPermission`, `createQueryWrapper`, harness `conPermisos/limpiarAuth` de CeCo en pruebas | Búsqueda, permisos y arnés de pruebas |
| `exceljs` (ya dependencia) | Lectura de `.xlsx` con importación dinámica |

Nuevos (sin equivalente en el repo): `InsigniasCuenta` (reglas de «Pendiente»), `ArbolCuentas` (el árbol de CeCo es específico de 3 niveles/dimensiones, no parametrizable sin reescribirlo; se siguió su patrón ARIA tree y lazy), `CuentaForm`, `PerfilReporte`, `VistaPreviaTabla`, `ConfirmarEstatusCuenta` (el de CeCo está atado a cascada de dimensiones), `manejarErrorCuenta` (el handler de CeCo invalida el namespace de CeCo; los handlers son por-feature por convención). El Sheet usa `window.confirm` al cerrar con `isDirty`, igual que los providers de CxC/Compras.

### 3. Estados visibles → prueba

| Estado / requisito | Prueba |
|---|---|
| Cargando (árbol, detalle) | `CatalogoPage › cargando…`; `CuentaDetallePage › cargando…` |
| Vacío con CTA importar | `CatalogoPage › vacío…` |
| Sin resultados con filtros (no confundir con vacío) | `CatalogoPage › lista sin resultados…` |
| Guardando | `CuentaForm › guardando…` (botón «Guardando…» deshabilitado) |
| Guardado: toast solo tras 2xx, Idempotency-Key | `CuentaForm › guardando…guardado`; `CuentaDetallePage › guardado: PUT con If-Match…`; baja y aplicar importación |
| Error de validación 422, el formulario conserva datos | `CuentaForm › 422…`; `CuentaDetallePage › 422 CAMBIO_BLOQUEADO_POR_USO` |
| Validación de cliente | `CuentaForm › validación de cliente…` |
| Sin permiso (sin acciones de escritura) | `CatalogoPage › sin permiso de escritura…`; `CuentaDetallePage › sin permiso de administrar…`; `ImportacionPage › sin permiso de importar…` |
| Conflicto 409 (recargar sin sobrescribir, borrador conservado) / 428 | `CuentaDetallePage › conflicto 409…`, `› 428…` |
| Fallo recuperable (reintentar sin perder datos) | `CatalogoPage › fallo recuperable…`; `CuentaDetallePage › fallo recuperable…`; `CuentaForm › fallo recuperable (500)…`; `ImportacionPage › fallo recuperable en el perfilado…` |
| Insignia «Pendiente» (naturaleza/tipo nulos, nunca valor supuesto) | `CatalogoPage › insignia pendiente…`; `CuentaDetallePage › naturaleza y tipo nulos…` |
| Árbol perezoso por `raizId`, insignia Inactiva | `CatalogoPage › cargando…` (expande y carga la hija) |
| Búsqueda con debounce 200 ms y filtro «pendientes» | `CatalogoPage › búsqueda con debounce…` |
| Cuenta usada: candado, explicación y campos bloqueados | `CuentaDetallePage › cuenta usada…` |
| Baja lógica con confirm y efectos; baja rechazada (hijas activas) | `CuentaDetallePage › baja lógica…`, `› baja rechazada…` |
| Importación: formato inválido; perfilado agrupado y «qué se reabre»; vista previa con acción/errores/sugerencia y filtro «solo con errores»; aplicar deshabilitado con errores | `ImportacionPage › formato no soportado…`, `› paso 2 perfilado…` |
| Importación: aplicar (huella + Idempotency-Key), resultado, «idempotente: ya aplicado», 422 al aplicar | `ImportacionPage › aplicar…`, `› resultado idempotente…`, `› 422 FILAS_CON_ERRORES…` |
| Lectura de archivo (BOM, Windows-1252, xlsx como texto, filas vacías conservadas); reporte sin contenido | `lib/archivo.test.ts` |

### 4. Resultados reales (2026-10-02, Node v24.4.1 en el worktree; `npm install` previo)

| Comando | Resultado |
|---|---|
| `tsc -b tsconfig.app.json tsconfig.node.json` | exit 0, sin errores |
| `npm run typecheck:test` (`tsc -b tsconfig.test.json`) | exit 0 (tras corregir un tipo en mi propia prueba) |
| `npm run lint` | exit 0: **0 errores, 10 warnings**, todos preexistentes en `modules/administracion` (ninguno en `features/contabilidad`) |
| `npm run build` | exit 0 («built in 32.90s»; solo el aviso habitual de chunks > 500 kB) |
| `vitest run src/features/contabilidad src/lib/nav.test.ts` | 56 pruebas en 6 archivos; 3 fallaron en la primera corrida (selector `getByLabelText(/Código/)` ambiguo con «Código agrupador»), corregido y reejecutado el archivo: pasa |
| `vitest run` (suite completa, 312 archivos) | **308 archivos / 1742 pruebas pasaron; 4 archivos fallaron** (`LineaInlineForm.idempotency`, `EditorLineas.cc`, `TarjetasPage.smoke`, `EmpleadosPage.smoke`: módulos ajenos, tiempos de espera por carga de la máquina). Reejecutados solos: **4 archivos / 15 pruebas, todos pasan** |

Notas: la corrida de vitest reescribe localmente dos snapshots de otros módulos (`EstadoBadge`, `NaturalezaBadge`, solo saltos de línea); se revirtieron con `git checkout` para no ensuciar el diff. `routeTree.gen.ts` aparece con diff completo por saltos de línea (CRLF/LF); el contenido nuevo son las 3 rutas de Contabilidad. Tras la última edición de una prueba (`CatalogoPage.smoke`, solo tipo) se verificó con `typecheck:test`, pero la suite completa no se repitió.

### 5. Pendiente y límites conocidos

- **Capturas de pantalla** (árbol, vista previa con errores, conflicto, sin permiso): NO generadas (no hay navegador/servicios que pueda usar sin tocar el backend y Vite del dueño). Pendiente de generar a mano contra el backend local.
- No se verificó el flujo de extremo a extremo contra el backend real (solo MSW con los contratos de `03`/`05`).
- La búsqueda global del topbar contextual no se cableó: el catálogo usa su propio campo con debounce de 200 ms (mismo enfoque que `BuscadorCatalogo` de CeCo).
- Se omitió el historial (auditoría) del detalle porque la API no lo expone, y la descarga de «plantilla de muestra» (el plan §10 la mencionaba): no hay plantilla ficticia versionada que servir.
- Sin master-detail de 320 px: se usó bandeja (P1) + página de detalle (P3), más simple y suficiente para el alcance.

### 6. Desviaciones y defectos del backend detectados (no corregidos)

1. `GET /cuentas/arbol` no devuelve `cuentaControl` ni `naturaleza`: en el árbol no se puede mostrar la insignia «Control»; sí aparece en lista y detalle.
2. `GET /cuentas/{id}` devuelve `usada` solo para la propia cuenta, pero el bloqueo R8 del `PUT` también se dispara si una **descendiente** está usada: la UI puede no mostrar el candado y recibir 422 `CAMBIO_BLOQUEADO_POR_USO` (se muestra el mensaje y se conserva el borrador).
3. No hay endpoint de ancestros ni de hijos: se resuelven con `GET /cuentas/{id}` encadenado y `GET /cuentas?padreId=` (hasta 200 hijas).
4. Los 422 de reglas de negocio llegan con `code` y `detail` pero **sin `errores[]` por campo**; la UI mapea código → campo con una tabla local (`lib/errores.ts`).
5. El 422 `CONTAB_IMPORT_FILAS_CON_ERRORES` trae `errores[]` con la forma de `ErrorFila` (fila/columna/codigo/mensaje), distinta de la convención `{campo,codigo,mensaje}` de `ProblemDetails`; se lee con un cast.
6. `perfilado` devuelve `Resumen/Distribuciones/Estructura/...` como `object` (sin esquema tipado): la UI muestra los escalares genéricamente y los campos conocidos (`porCodigoError`, `queSeReabre`, `columnasSinMapeo`, `pendientesValidacion`).

### 7. Design system (ronda 2: migración a `design-system/DESIGN.md`)

Leídos completos: `DESIGN.md`, `FIGMA.md`, `README.md`, `frontend/AGENTS.md` e `index.css`.

**Qué se migró**
- Todos los colores default y ajustados a tokens: `amber-*`/`slate-*` → variantes semánticas de `Badge` (`warning` para pendiente y tipo/naturaleza nulos, `neutral` para Título/Afectable/Inactiva/Usada, `info` para Control, `outline` para naturaleza, `danger`/`success`/`neutral` para la acción por fila de la vista previa) y `text-warning-fg`, `bg-warning-note-bg`, `text-warning-note-fg`, `border-warning`; los alias shadcn (`text-muted-foreground`, `text-destructive`, `bg-muted`, `text-primary`, `border-input`) → `text-ink-muted`, `text-danger-fg`, `bg-surface-subtle/muted`, `text-brand`, `border-line-control`.
- Contenedores de tabla y secciones: `border` → `rounded-lg bg-surface-card shadow-card-flat`; filas con `border-line-row`; encabezados con la receta 4.4 (`text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted`, `bg-surface-subtle`, `border-line-divider`).
- Controles: `<input type="checkbox">` → `Checkbox` + `Label` compartidos; `<select>` nativos con la receta de `Input` (`SELECT_CLASS`: h-36, `border-line-control`, foco de marca, mínimo táctil); botones de formularios y del asistente con `size="lg"` (h-36); «Desactivar» con `variant="secondary-danger"`; ancho del Sheet con el token `sm:max-w-sheet` (560 px, verificado en el CSS generado).
- Formulario: `Field` de centros-costo (usaba `rose-600` por defecto) sustituido por un `Field` local sobre el `Label` compartido, con «(opcional)» en campos opcionales y grid `gap-x-4 gap-y-3.5`.
- Avisos de campos bloqueados/cuenta usada: nuevo `AvisoNota` (callout 4.10, `role="note"`, tokens `warning-note`); reemplaza el `Alert` y el `<p>` con colores propios.
- Páginas: H1 `text-3xl` con descripción de una línea, padding `px-6 py-5` (plantilla 5.1), íconos con `strokeWidth` 1.6 y `size-*`.
- Componentes propios que se conservan: no existe en el repo un equivalente compartido para insignias de cuenta (`NaturalezaBadge`/`EstadoBadge` son de Compras, con enums y colores default propios), así que `InsigniasCuenta` se reescribió sobre `Badge`; `ArbolCuentas`, `CuentaForm`, `PerfilReporte`, `VistaPreviaTabla` y `ConfirmarEstatusCuenta` usan Button/Badge/Checkbox/Label/Alert/AlertDialog/Sheet/Skeleton compartidos.

**Checklist de la sección 9**

| Punto | Estado |
|---|---|
| Dentro del App Shell, con el ítem activo en rail y panel | Cumple en lo que depende del shell (las rutas viven bajo `_app`; Contabilidad es módulo del registro de navegación con 2 cards). **Pendiente** verificar visualmente el ítem activo en el navegador |
| Breadcrumb refleja la ubicación | **Pendiente**: lo resuelve el shell; no se comprobó en navegador que las 3 rutas nuevas produzcan el breadcrumb esperado |
| Usa una plantilla de la sección 5 sin layouts inventados | **Parcial**: bandeja y detalle usan H1 + descripción + tarjeta de tabla, pero sin KPIs, tabs de vista ni chips de filtro (5.1), y el detalle no tiene lista maestra de 320 px (5.2); el Sheet sí es el de 4.8 en ancho pero sin footer fijo con «Guardar borrador». No cumple la plantilla completa |
| Sin hex, tamaños ni radios fuera de tokens | **Cumple** en colores: `grep` de paleta default/hex en `features/contabilidad` y `routes/_app/contabilidad` (sin pruebas) = 0. Quedan los valores arbitrarios que la propia guía prescribe (`tracking-[0.04em]`) y `min-w-[560px]/[640px]` de las tablas |
| Plex Sans 13 base; folios/UUID/RFC en Plex Mono | **Cumple**: cuerpo `text-sm`; códigos de cuenta en `font-mono` |
| Montos con `tabular-nums` a la derecha en `$0,000.00` | **No aplica**: el módulo no muestra montos (sí conteos; sin `tabular-nums` en conteos) |
| Estados con los badges y textos exactos de 7.2 | **Parcial**: se usan variantes semánticas de `Badge`, pero 7.2 no define estados para cuentas contables; los textos («Inactiva», «Pendiente de validación», etc.) son del dominio y deben validarse con Contabilidad |
| Tabla de 10 filas, estirada al alto disponible | **No cumple**: la lista pagina de 50 en 50 (`limit` 50, como el resto de la API de catálogos) y no se estira al alto; la paginación es «Anterior/Siguiente» |
| Una acción primaria por zona; botones deshabilitados explican el motivo | **Cumple**: «Aplicar importación» y el toggle «Árbol» deshabilitados llevan `title` y texto cercano |
| Botones de ícono con `aria-label`, inputs con `<label>`, sin `onClick` en `div` | **Cumple**: chevrons del árbol con `aria-label`; campos con `Label`/`aria-label`; ningún `onClick` en `div` |
| Español (es-MX), sentence case, sin datos inventados | **Cumple** (datos solo `FIX-*` en pruebas) |

**Pendiente del design system**
- Comparar con Figma en navegador y capturas de pantalla (no generadas; mismo motivo que arriba).
- Los `<select>` siguen siendo nativos con la receta de Input; migrarlos al `Select` (Radix) compartido exige reescribir las pruebas que usan `fireEvent.change`.
- Composición completa de plantillas 5.1/5.2 (KPIs, tabs, chips de filtro, lista maestra, sheet con footer fijo).
- Modo oscuro: no migrado (igual que el resto del sistema).
- `frontend/src/lib/nav.test.ts:348` sigue con un error de tipos preexistente de main (`string` no asignable a `"compras.ordenes.leer" | "compras.ordenes.autorizar-nivel1"`); no se tocó.

**Verificación de esta ronda (reales):** `grep` de paleta default/hex/alias shadcn en los dos directorios = 0 coincidencias; `vitest run src/features/contabilidad src/lib/nav.test.ts src/components/layout` = 11 archivos / 84 pruebas pasan; `tsc -b tsconfig.app.json` exit 0; `tsc -b tsconfig.test.json` = solo el error preexistente `nav.test.ts(348,58)`; `eslint` sobre contabilidad (features y rutas) exit 0 sin avisos; `npm run build` exit 0.

### 8. Ronda 3 — lector de .xlsx con varias hojas, título y filas vacías

Decisión del TL: la hoja correcta es «Plan de cuentas-VILO». Estructura real (solo estructura): libro de 3 hojas; la hoja correcta es la 3.ª, con un título en la fila 1, los encabezados en la fila 2 (incluida una columna vacía) y filas vacías intercaladas. Antes, `archivo.ts` leía `worksheets[0]` y tomaba la fila 1 como cabecera.

**Qué cambió (solo frontend, `features/contabilidad`)**
- `lib/archivo.ts`: `leerArchivo` se divide en `abrirArchivo` (lee todas las hojas como TEXTO; devuelve nombre y filas con datos por hoja), `hojaSugerida`, `detectarEncabezado` y `prepararHoja`. Se normalizan las cabeceras igual que el servidor (`FormatoCatalogo.NormalizarCabecera`: sin acentos, minúsculas, espacios y guiones a `_`).
- Selector de hoja (`ImportacionPage`): si el .xlsx tiene más de una hoja, antes del perfilado se muestra «Hoja del libro» con «nombre — N filas con datos» y el botón «Analizar hoja»; se preselecciona la hoja cuyo nombre contiene «plan de cuentas» y tiene encabezado reconocible (si hay varias, la de más columnas reconocidas; si ninguna, la primera). Con una sola hoja no hay paso extra.
- Detección del encabezado: usa los alias de `GET /api/v1/contabilidad/configuracion-formato` (`importacion.columnas`) y elige la primera fila, dentro de las primeras 15, con al menos 2 columnas canónicas distintas. Si ninguna coincide (o no se pudo leer la configuración), usa la fila 1 y lo avisa. Si el encabezado no está en la fila 1, avisa «Encabezado detectado en la fila N; las N-1 fila(s) anteriores se ignoraron».
- Números de fila: el cuerpo conserva las filas vacías posteriores al encabezado. El servidor numera con cabecera = 1, así que el cliente suma un `desplazamiento` (fila del encabezado - 1) para mostrar la fila real del archivo en la vista previa, el perfilado (ejemplos), los errores al aplicar y el reporte descargable. **El backend no se modificó.**
- Reglas sin cambio: perfilado y vista previa no escriben; Idempotency-Key, huella, permisos y estados visibles igual.

**Pruebas (fixtures FIX-* generados en la prueba con exceljs)** — `lib/archivo.test.ts` y `pages/ImportacionPage.test.tsx`: libro de 3 hojas con título en la fila 1, encabezado en la fila 2, filas vacías y columna vacía en el encabezado. Cubren: lista de hojas con nombre y filas con datos; preselección de «Plan de cuentas-VILO» frente a la 1.ª hoja con el mismo texto en el nombre; encabezado detectado en la fila 2; título descartado; números de fila del servidor + desplazamiento = filas 4 y 7 del archivo; vista previa que muestra la fila 4 (servidor 3) y no la 3; una sola hoja con encabezado en la fila 1 sin selector y con desplazamiento 0; sin encabezado reconocible usa la fila 1 y avisa; límite de 15 filas; reporte descargable con filas reales y sin valores de celda.

**Resultados reales:** `vitest run src/features/contabilidad` = 5 archivos / 47 pruebas pasan; `vitest run src/lib/nav.test.ts src/components/layout` = 6 archivos / 49 pruebas pasan; `tsc -b tsconfig.app.json` exit 0; `tsc -b tsconfig.test.json` = solo el error preexistente `src/lib/nav.test.ts(348,58)`; `eslint` en contabilidad (features y rutas) exit 0 sin avisos; `npm run build` exit 0.

**Hallazgos para el TL (no corregidos)**
1. **CSV con título antes del encabezado:** el backend (`LectorTabla.Leer`) toma SIEMPRE el primer registro como cabeceras. Un CSV real con una fila de título (como el .xlsx) falla con 422 `CONTAB_IMPORT_COLUMNA_FALTANTE` y la lista de cabeceras encontradas (la del título). No hay tolerancia en el servidor, y el cliente no toca el CSV (se envían los bytes tal cual). Cambio mínimo propuesto si se necesita: en `LectorTabla`, tras decodificar el CSV, saltar los primeros registros hasta la primera fila que cumpla ≥2 alias canónicos (máx. 15) y numerar las filas con el desplazamiento correspondiente; alternativa sin tocar el backend: pedir a Contabilidad el CSV sin la fila de título o exportar como .xlsx.
2. **Numeración del servidor:** con `columnas`+`filas` la numeración es relativa a la cabecera (cabecera = 1). El desplazamiento en el cliente la corrige; un campo opcional `filaEncabezado` en `ImportacionRequest` permitiría que el servidor devuelva directamente la fila real y que también aplique a los mensajes del servidor que incluyan «fila N» en el texto (hoy esos textos, si los hubiera, no se corrigen en el cliente).
3. **Preselección ambigua:** si la hoja 1 (otra numeración) también se llamara «Plan de cuentas…» y tuviera encabezado reconocible con más columnas que la hoja VILO, se preseleccionaría esa; el usuario ve el selector y puede cambiarla.
4. **Columna vacía en el encabezado:** el servidor la tolera (advertencia «columna N sin nombre», no se importa).
5. Pendiente: no se probó con el archivo real (no se leyó, por instrucción) ni contra el backend real.



## Alta manual: cuenta padre como combobox y código sugerido (2026-10-02)

- **Cuenta padre.** El campo pasó de "caja de búsqueda + `<select>` nativo" (dos controles, sin etiqueta visible el primero) a un único combobox (`CuentaPadreSelector`) con los componentes del ERP (`Popover` + `Command` + `CommandInput`, mismo patrón que `components/erp/selectors`). Búsqueda en servidor con debounce de 200 ms; solo títulos activos; opción «Sin padre — cuenta raíz».
- **Opción 2 (P18).** Al elegir el padre se autollena el siguiente código libre de su rama, editable; no pisa un código que el usuario ya escribió. Validación de rama al crear (`CONTAB_CUENTA_CODIGO_FUERA_DE_RAMA`), configurable. No aplica al editar.
- **Pruebas.** Unitarias de Contabilidad: 86 (17 nuevas de rama y siguiente código: ancho fijo, ancho libre, inactivas, rama llena, otra rama, nivel saltado). Vitest de Contabilidad: 51 (combobox, autollenado, motivo sin sugerencia, error de rama en el campo Código). Integración: prueba HTTP nueva de sugerencia + rechazo fuera de rama + padre afectable.
- **Pendiente de Contabilidad.** Confirmar la regla de numeración y si se permite mover cuentas entre padres (hoy permitido si no están usadas).

## Ronda «reglas de Laura» y respuesta del TL (2026-10-02/03)

Insumo: «Catalogo de cuenta propuesta Millet.xlsx» (hoja «Plan de cuentas», encabezado en fila 2) y handoff del TL. Del archivo real solo se documentan estructura y estadísticas (plan §18 P19–P27).

- **Afectabilidad derivada (P19) y conversión dinámica (P20):** nivel 1 y cuentas con hijas acumulan; nivel ≥2 sin hijas es afectable. Una hija nueva bajo una afectable sin movimientos la convierte en acumula; con movimientos se rechaza. En la UI el tipo es informativo.
- **Filas de título sin código (P21)** se omiten con aviso; **Reporte** se conserva en `grupo_reporte` (P22).
- **Colectivas ampliadas (P23):** Deudores y Acreedores; supuesto configurable Clientes/Deudores ⇒ CxC, Proveedores/Acreedores ⇒ CxP; captura manual siempre rechazada.
- **Rubros (P24, propuesta del TL):** agrupación de reporte separada del árbol (`clase` + `rubro_id` en raíces de nivel 1); en importación cada rubro agrupa las raíces que le siguen en el archivo hasta el siguiente rubro; «Acumula rubro» igual que rubro.
- **Agrupaciones de presentación (P25)** sin exigencias de cuenta afectable; **padre explícito** para el caso de la fila 47 (P26); **13 caracteres** se conservan (P27, se retira la homologación).
- **Panel «Probar si la cuenta acepta movimientos»** en el detalle (usa `POST /cuentas/validar-movimiento`) para demostrar el rechazo de movimientos directos en cuentas que acumulan, rubros y colectivas.
- **Migración:** `20261003001040_ContabilidadRubrosYTipoDerivado` (aditiva: `clase`, `rubro_id` + índice, FK y check; recalcula `tipo`). Aplicada en `millet_dev` (historial 251).
- **Muestra ficticia:** `5-FIX-formato-Laura.xlsx` (fuera del repo, en la carpeta local de pruebas FIX).
- **Brecha con módulos auxiliares:** ni CxC ni CxP consumen hoy `ICuentaContableReadPort` ni registran asientos (revisión de solo lectura): la restricción de colectivas está lista en el puerto, pero ningún módulo la invoca todavía.

**Resultados reales:** unitarias Contabilidad 109/109; integración en BD desechable (`validate-integration-isolated.sh`, filtro Contabilidad) 44/44; Vitest Contabilidad + navegación 85/85; lint del módulo sin avisos; typecheck de la app limpio; 0 colores fuera del design system.

**Pendientes (propuestas del TL, no definitivas):** configuración definitiva de rubros; significado de «Acumula rubro»; asignación real de colectivas y su módulo; relación definitiva del caso de la fila 47; modalidad de aprobación de la importación (única decisión funcional pendiente); 54 naturalezas vacías se conservan vacías.
