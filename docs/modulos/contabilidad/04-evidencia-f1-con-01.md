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
3. **Validez de los códigos que rompen el patrón.** Doce códigos tienen 13 caracteres (último segmento de 3 dígitos) frente a 12 de los demás (999.99.99.99).
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
