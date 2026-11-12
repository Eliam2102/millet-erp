# Feature Specification: Cierre verificable de recepción e hitos de Comercio Exterior

**Feature Branch**: `fix/n15-ce-conciliacion`

**Created**: 2026-11-11

**Status**: Ready for planning

**Input**: User description: "Adelante… usar siempre speckit para planificar… ayudar a cerrar esa parte".
Continúa N-15/P25: corregir la conciliación observada en QA y completar el recorrido pendiente.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cantidad recibida consistente (Priority: P1)

Como responsable de Comercio Exterior, necesito que las recepciones parciales de varias
órdenes coincidan con la mercancía realmente recibida, aunque los avisos lleguen en otro orden.

**Why this priority**: Hoy una orden recibida2/2 aparece1/2 en CE y bloquea el cierre legítimo.

**Independent Test**: Recibir dos parcialidades de una orden y procesar los avisos invertidos;
la recepción y la incidencia deben terminar iguales al procesamiento cronológico.

**Acceptance Scenarios**:

1. **Given** dos recepciones firmes de una unidad, **When** llega el aviso de la segunda
   antes del de la primera, **Then** CE mantiene dos unidades y cero faltantes.
2. **Given** un aviso ya procesado, **When** se repite, **Then** no duplica cantidades,
   incidencias, historia ni avisos a responsables.
3. **Given** una disminución legítima del recibido, **When** se concilia la operación,
   **Then** se refleja el neto menor y su diferencia real; no se conserva un máximo histórico.
4. **Given** varias guías/expedientes de la misma orden, **When** se actualiza el recibido,
   **Then** se conserva el reparto vigente sin duplicar unidades ni reactivar unidades canceladas.

### User Story 2 - Responsable válido sin ampliar permisos (Priority: P2)

Como usuario autorizado del ERP, necesito registrar hitos sólo con responsables que tengan
acceso válido a la sucursal, usando las reglas de acceso existentes.

**Why this priority**: El ensayo devuelve responsable inválido para Eliam y detiene los hitos.

**Independent Test**: Comparar un responsable operativo asignado, uno corporativo autorizado,
un usuario inactivo y otro sin acceso; aceptar o rechazar según su alcance efectivo existente.

**Acceptance Scenarios**:

1. **Given** un responsable activo con acceso legítimo a la sucursal, **When** registra un
   hito con evidencia propia, **Then** se acepta conservando actor y responsable.
2. **Given** un usuario sin ese acceso o inactivo, **When** se propone como responsable,
   **Then** se rechaza antes de guardar.
3. **Given** un dato de configuración ausente, **When** se prepara el ensayo,
   **Then** se identifica el cambio mínimo y su autorización; no se otorgan accesos silenciosamente.

### User Story 3 - Recuperar y cerrar el ensayo existente (Priority: P3)

Como coordinador, necesito repetir los cierres sobre el expediente de muestra ya creado,
conservar la evidencia de la falla y saber exactamente qué falta para cerrar N-15.

**Why this priority**: Reiniciar o cancelar artificialmente la incidencia ocultaría el defecto.

**Independent Test**: Reconciliar el expediente CE-IMP-2026-000004 usando sus seis recepciones
existentes, completar hitos/documentos y verificar sus tres cierres y aprobación final.

**Acceptance Scenarios**:

1. **Given** una conciliación histórica incorrecta, **When** se actualiza desde las recepciones
   reales, **Then** se corrige la cantidad y se cierra sólo la diferencia que efectivamente desapareció,
   conservando historia y sin crear otra recepción.
2. **Given** tres órdenes autorizadas, dos guías sin contenedor y seis recepciones,
   **When** se consulta el reporte, **Then** aparecen seis asociaciones correctas de orden,
   recepción, guía y clave, sin inventar número de contenedor.
3. **Given** hitos y soportes completos sin diferencias reales, **When** se ejecutan los tres
   cierres y la aprobación final, **Then** el expediente se cierra y queda evidencia por paso.
4. **Given** una comprobación pendiente, **When** se informa el avance,
   **Then** N-15 sigue abierta y se distingue prueba con muestras de aceptación de Millet.

### Edge Cases

- Avisos distintos con la misma fecha/hora; duplicados; entrega concurrente o invertida.
- Recepción cancelada o parcialmente devuelta; disminución posterior válida; cantidades negativas inválidas.
- Más de una guía y expediente por línea; expediente cancelado/cerrado; remanentes y sobreembarque.
- Datos de conciliación anteriores al cambio; ninguna recepción; fuente temporalmente indisponible.
- Responsable activo/inactivo, sucursal ajena, acceso operativo y corporativo; actor diferente del responsable.
- Evidencia de otro expediente o dada de baja; cierre documental sin despacho/liberación.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: CE debe reflejar el recibido vigente sin depender del orden de llegada de avisos.
- **FR-002**: El mismo aviso repetido no debe duplicar efectos; avisos inválidos se rechazan sin cambios.
- **FR-003**: El neto debe respetar cancelaciones/devoluciones válidas, incluso si disminuye.
- **FR-004**: Se conserva el reparto vigente por arribo/creación y la separación empresa/sucursal.
- **FR-005**: Las diferencias sólo se cierran automáticamente cuando el neto vuelve a cuadrar;
  faltantes/sobrantes reales siguen bloqueando los cierres que corresponden.
- **FR-006**: Debe existir recuperación verificable del caso histórico sin inventar avisos o recepciones.
- **FR-007**: La elegibilidad de responsables debe reutilizar el alcance efectivo existente y no ampliar permisos.
- **FR-008**: Se conservan los controles de documentos, separación de funciones y tres cierres/aprobación.
- **FR-009**: La prueba debe reutilizar las tres órdenes, seis recepciones y soportes ficticios actuales.
- **FR-010**: Registrar versión probada, resultado por tramo, incidencias y límites; mantener N-15 abierta
  hasta verificar el recorrido. El cierre de P25 no significa cierre fiscal/productivo de todo CE.

### Key Entities

- **Recepción**: entrada firme y sus líneas, orden de origen, cantidad, estado y posibles reversas.
- **Partida de importación**: asignación de una línea de orden a una guía; conserva cantidad y recibido.
- **Incidencia**: diferencia real, responsable e historia; se mantiene si no cuadra el neto.
- **Responsable**: usuario activo con alcance operativo/corporativo válido para la sucursal.
- **Expediente y cierres**: operación de varias órdenes, hitos, soportes y aprobación final.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: El 100% de escenarios cronológico, invertido, repetido, concurrente y empatado
  termina con el mismo recibido y diferencias para la misma mercancía vigente.
- **SC-002**: Una disminución válida se refleja exactamente, sin sostener un máximo obsoleto.
- **SC-003**: El expediente de QA conserva exactamente tres órdenes, dos guías y seis recepciones;
  la primera orden termina2/2 y el reporte conserva seis filas con contenedor no aplicable.
- **SC-004**: Los cuatro perfiles de responsable del ensayo producen aceptación/rechazo conforme
  al acceso vigente, sin asignaciones de permisos adicionales no autorizadas.
- **SC-005**: Las tres dimensiones del cierre y la aprobación final tienen resultado verificable;
  mientras falte cualquiera, la tarea sigue abierta y el pendiente está identificado.

## Assumptions

- Requisitos confirmados: continuar el alcance P25 ya recibido; no pedir nuevos datos a Millet.
- Las recepciones del ensayo son ficticias, exclusivas de QA y no deben emitirse ni pagarse.
- Se reutilizan autenticación, alcance, reglas documentales y matriz vigente; no se solicita
  consentimiento nuevo de Azure CLI para probar el ERP por la sesión existente.
- La causa técnica precisa del desfase y la elegibilidad corporativa se investigan en el plan.
- Los pases y pruebas autorizados se separan de nuevas ampliaciones de acceso que requieran confirmación.
- Fuera de alcance: rehacer P25, OCR, operación productiva, cierre total de CE y nuevas reglas fiscales.
