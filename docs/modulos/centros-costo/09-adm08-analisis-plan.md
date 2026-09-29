# F1-ADM-08 - Analisis y plan de cierre tecnico

**Fecha objetivo:** 2026-09-29 11:00, America/Merida
**Estimacion comprometida:** 6 h (4.5 h construccion + 1.5 h pruebas)
**Responsable:** Uziel
**Estado:** Fuente M1 recibida; correspondencia registrada y conciliacion tecnica en curso

## Objetivo

Cerrar tecnicamente el catalogo jerarquico de centros de costo reutilizando el modulo `Millet.CentrosCosto` y su modelo `Dim1 -> Dim2 -> Dim3`. No se creara otro catalogo ni se relacionara automaticamente con sucursales o departamentos del ERP.

La aprobacion de Contabilidad/Millet sobre datos reales permanece como una puerta posterior e independiente.

## Fuentes revisadas

- Implementacion backend de `Millet.CentrosCosto`.
- Endpoints de catalogo, jerarquia y asignaciones.
- Migraciones y siembra vigente del esquema `centros_costo`.
- Pantallas de configuracion y asignacion.
- Pruebas de integracion de CRUD, jerarquia, alcance, baja y selectores.
- ADR-0050 y documentacion de consumo en Compras y Almacen.
- Documentacion local y handoff de Fase 1.
- `Propuesta Cecos ERP_M1.xlsx`, hojas `CeCo` y `NumEq`, recibido el 2026-09-29.

No fue posible abrir la ficha ADM-08 de Notion: los enlaces conocidos responden que la pagina no existe o no esta disponible para la cuenta actual.

## Hallazgo principal sobre los datos

La fuente historica documentada es `Prop_Cecos_MIndustria_Jul25.xlsx` y la siembra actual contiene:

| Elemento | Cantidad actual |
| --- | ---: |
| Dim1 | 5 |
| Dim2 | 57 |
| Dim3 | 361 |
| GrupoDim2 | 6 |
| GrupoDim3 | 44 |

La propuesta M1 recibida contiene **49 CeCos** y **352 NumEQ**. La comparacion con la siembra vigente encontro 33 claves CeCo presentes, 16 claves ausentes, 24 claves antiguas fuera de la propuesta, 352 NumEQ en la fuente y 361 Dim3 sembradas. La correspondencia detallada y las decisiones pendientes estan en `10-adm08-correspondencia-m1.md`.

## Cobertura AS-IS contra el criterio de cierre

| Criterio | Cobertura existente | Accion prevista |
| --- | --- | --- |
| Crear y consultar con jerarquia valida | Commands, queries y pruebas existentes | Ejecutar recorrido API/UI y conservar evidencia |
| Rechazar clave duplicada | Restriccion global y `CECO_CLAVE_DUPLICADA` | Verificar API con prueba existente |
| Rechazar padre invalido | Guards de Dim2/Dim3 y pruebas de dominio/integracion | Completar caso HTTP si solo esta probado via MediatR |
| Rechazar edicion conflictiva | Version esperada y `ConcurrencyException` | Verificar respuesta HTTP 409/ETag |
| Desactivar sin borrar historico | Baja logica en cascada; read-port incluye inactivas | Probar referencia historica y selector nuevo por separado |
| Ofrecer solo centros vigentes | Selectores filtrado y abierto excluyen inactivas por defecto | Ejecutar pruebas existentes y revisar todos los consumidores |
| Respetar alcance/contexto | Asignacion usuario->Dim3 y permiso `dim3.leer-todos` | Confirmar que cada endpoint consumidor usa el selector correcto |
| Rechazar usuario sin permiso en API | Endpoints con permisos canonicos y pruebas parciales | Agregar solo los casos negativos faltantes |
| Documentar operaciones consumidoras | ADR-0050 y documento de consumo existentes | Consolidar tabla para el PR sin activar reglas nuevas |
| Correspondencia de 49 CeCos | Fuente M1 recibida y comparada | Aplicar solo renombres inequívocos; escalar ambiguedades |

## Resultado de la linea base

- El build de `Millet.Api.IntegrationTests` termina con 0 errores y 0 advertencias.
- El gate aislado final aprobo 33 pruebas relacionadas con CentrosCosto y selectores abiertos, incluida la nueva cobertura HTTP.
- Las 55 pruebas frontend del modulo aprobaron al ejecutarse con un solo worker. La primera ejecucion con procesos separados aprobo sus 36 pruebas iniciadas, pero Vitest no pudo levantar cuatro workers dentro del entorno; no fue un fallo funcional.
- Se agrego una prueba HTTP integral para autenticar los criterios de alta, consulta, clave duplicada, padre inexistente o inactivo, `ETag`/`If-Match`, conflicto de version, baja logica, lectura historica, selectores vigentes, alcance y permisos.
- El script de integracion ahora elimina `CR` del manifiesto de migraciones. Esto permite ejecutarlo desde Git Bash cuando el archivo tiene finales de linea de Windows.

### Brechas confirmadas

1. La matriz de 49 CeCos ya esta disponible. Persisten decisiones funcionales sobre renombres, vigencias y registros que desaparecen de la propuesta; no se asumiran bajas definitivas sin aprobacion de Contabilidad/Millet.
2. El catalogo es global por diseno y no contiene `EmpresaId`. En el modelo operativo actual de una sola empresa, el contexto de empresa participa en RBAC; el alcance de seleccion se controla por asignaciones de usuario y `centros_costo.dim3.leer-todos`.
3. Los selectores impiden ofrecer Dim3 inactivas y respetan el alcance aplicable. Sin embargo, al guardar una linea de requisicion, Compras solo exige un `CentroCostoId`; no vuelve a comprobar que exista, este activo y pertenezca al alcance. Esta validacion de escritura requiere acordar un puerto de validacion entre Compras y CentrosCosto y debe estimarse como correccion de consumidor.
4. M1 cambia el padre de cuatro NumEQ frente a la siembra vigente: `VU056`, `CHDIR01`, `VU106` y `VV060`. El modelo declara el padre inmutable; automatizar el cambio reinterpretaria referencias historicas. Requiere decidir entre conservar el registro, o baja logica mas alta con nueva identidad.
5. No se genera una migracion de conciliacion mientras sigan pendientes los renombres, bajas, vigencias y reubicaciones. La propuesta M1 no incluye estado ni fechas de vigencia, y la tarea prohibe inventarlos.

## Avance por fase

| Fase | Estado | Resultado |
| --- | --- | --- |
| 1. Linea base y recorridos | Completa | Backend, frontend y recorridos HTTP verificados |
| 2. Correspondencia de datos | Completa | 49 CeCos y 352 NumEQ comparados contra la siembra vigente |
| 3. Correcciones minimas | Completa para recorridos confirmados | Compatibilidad del gate en Windows corregida; sin fallos funcionales del catalogo; cambios de datos diferidos por decision funcional |
| 4. Pruebas y regresion | Completa para el codigo disponible | Cobertura positiva, negativa, historica, alcance y RBAC aprobada |
| 5. Entrega tecnica | Parcial | Evidencia tecnica lista; aceptacion de datos M1 y brecha de validacion en escritura siguen separadas |

## Operaciones consumidoras identificadas

| Operacion | Regla actual | Obligatoriedad documentada |
| --- | --- | --- |
| Linea de requisicion | Usuario elige Dim3 dentro de su alcance | Obligatoria |
| Linea manual de orden de compra | Comprador elige entre Dim3 activas mediante selector abierto autorizado | Obligatoria |
| Orden de compra desde requisicion | Hereda Dim3 de la linea de requisicion | Solo lectura |
| Entrada de almacen desde OC | Hereda Dim3 de la linea de OC | Solo lectura |
| Salida con requisicion | Hereda Dim3 de la linea de requisicion | Solo lectura |
| Vale urgente | Almacenista elige entre Dim3 activas mediante selector abierto autorizado | Obligatoria |

No se activara obligatoriedad adicional por cuenta o documento sin aprobacion expresa de Millet.

## Plan de ejecucion

### 1. Linea base y recorridos actuales - 1.0 h

- Levantar backend, frontend y base integrada ADM-01/02 desde esta rama.
- Ejecutar las pruebas de CentrosCosto mediante `tools/validate-integration-isolated.sh`.
- Ejecutar las pruebas frontend enfocadas de configuracion, jerarquia y asignaciones.
- Registrar fallos reproducibles antes de editar codigo.

### 2. Correspondencia de datos - 1.0 h

- Importar la fuente de 49 CeCos sin alterar el archivo original.
- Construir una tabla con clave, nivel, padre, estado y destino organizacional declarado.
- Comparar cada fila con Dim1-Dim3 y marcar `coincide`, `falta`, `ambiguo` o `conflicto`.
- No inferir responsables, vigencias, relaciones organizacionales ni reglas contables.

### 3. Correcciones minimas - 2.5 h

- Corregir solo recorridos que fallen contra los criterios de cierre.
- Reutilizar commands, handlers, validadores, permisos y componentes existentes.
- Mantener claves globalmente unicas, padre inmutable, Version/ETag y baja logica.
- Evitar migraciones salvo que la matriz aprobada requiera datos nuevos y exista una regla de carga confirmada.

### 4. Pruebas y regresion - 1.5 h

- Positivos: alta, consulta, edicion y jerarquia valida.
- Negativos: clave duplicada, padre inexistente/inactivo y version conflictiva.
- Historico: baja sin borrado y resolucion de referencias inactivas existentes.
- Seleccion: excluir inactivas y respetar alcance o permiso proxy segun el endpoint.
- Seguridad: 401 sin autenticacion y 403 sin permiso en API.
- Adjuntar comandos, resultados y tabla de correspondencia a la descripcion del PR.

## Regla de salida

La tarea solo se marcara como cierre tecnico cuando todos los casos anteriores pasen y la tabla de los 49 CeCos este incluida. Si la fuente no llega o un caso no cabe en seis horas, se registrara la brecha con evidencia, impacto y reestimacion; no se declarara completada.

## Reestimacion de brechas

Una vez recibidas las decisiones de Millet, la conciliacion de datos requiere aproximadamente 2 h adicionales: 1 h para preparar una migracion historica segura y 1 h para ejecutar migracion, regresion y evidencia. La validacion autoritativa al guardar documentos consumidores debe estimarse por separado porque afecta contratos entre Compras, Almacen y CentrosCosto.

## Dependencias pendientes

1. Confirmacion de Contabilidad/Millet para renombres, vigencias y registros ausentes de la propuesta M1.
2. Confirmacion posterior sobre jerarquia definitiva, combinaciones permitidas y obligatoriedad por cuenta/documento.
