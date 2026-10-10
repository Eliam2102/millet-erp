# ADR-0062: Heredar el centro de costo del departamento y permitir máquina opcional

- **Estado**: Aceptada para construcción por Eliam; equivalencias y aceptación de Millet **Por confirmar**.
- **Fecha**: 2026-10-09
- **Decisor**: Eliam, siguiendo la aclaración P203 de Millet.
- **Etiquetas**: ADM08, centros-costo, departamentos, compras, almacén, alcance
- **Reemplaza parcialmente**: [ADR-0050](./0050-consumo-centro-costo-maquina-dim3.md).

## Contexto y autoridad

El Documento maestro de Millet, aclaración **P203**, establece: «La estructura es jerárquica: sucursal, departamento y equipo o máquina. El centro de costo se consume en Compras: se hereda del departamento (sólo lectura) o se elige explícitamente cuando el usuario tiene alcance para ello». P200 mantiene la asignación del centro de costo a la máquina o equipo en CEN-01.

La decisión de Eliam del **09-oct-2026**, incluida en el paquete F1-ADM-08, es aplicar P203: herencia del departamento, máquina opcional y elección si existe alcance. Prevalece sobre la decisión interna de julio en ADR-0050 que hacía obligatoria la máquina por línea y descartaba el vínculo departamento–centro de costo. La equivalencia definitiva queda **Por confirmar** con Laura, V49, fecha prevista 16-oct según el paquete; no implica que esa validación haya ocurrido.

## Decisión

1. Mantener el catálogo Dim1 → Dim2 → Dim3. Crear una equivalencia por empresa, sucursal y departamento hacia una **Dim1 o Dim2 activa**. No agregar un catálogo duplicado ni identificar automáticamente departamentos por semejanza de nombres.
2. En la RQ, resolver el departamento del **requisitante**, usando su empleado activo. Para RQ de sistema o usuarios legados sin ficha de empleado, usar el departamento ya validado de la cabecera. Un empleado existente sin departamento no utiliza ese fallback: requiere equivalencia/elección.
3. Al capturar o editar una línea en borrador, prellenar el centro del departamento. Sin alcance para elegir, es solo lectura y el backend rechaza cualquier sustitución. Con alcance, puede elegir un centro o una máquina activa de su alcance. No se exige Dim3: el campo `CentroCostoId` almacena el nodo elegido/heredado de cualquiera de los tres niveles.
4. Conservar la asignación congelada usuario → Dim3. Para elegir un centro superior, habilitar únicamente los ancestros de las máquinas activas ya asignadas. Esto no concede máquinas hermanas ni máquinas agregadas después. El permiso existente de alcance total mantiene su semántica.
5. Priorizar la equivalencia del departamento. Sin equivalencia, una única máquina del alcance puede prellenarse; si no hay elección válida, rechazar con «Tu departamento no tiene centro de costo asignado; pídelo a Contabilidad».
6. Al transmitir, validar vigencia del centro guardado, sin recalcular la equivalencia. El estado de la RQ congela la edición estructural. OC desde RQ, entrada desde OC y salida con RQ copian el valor guardado, sin aplicar el alcance del comprador/almacenista al dato heredado.
7. La OC manual mantiene su captura abierta, autorizada por `compras.ordenes.crear-sin-rq`, incluyendo ahora Dim1/Dim2. Conservar validaciones de existencia, vigencia y alcance cuando corresponde (G1.11, P2, P7), incluyendo la vigencia de los padres.
8. Mantener la lectura histórica sin filtro de alcance, con resolución de centros inactivos y de los tres niveles. No exigir una máquina para identificar/agrupar un centro de departamento. El adaptador contable existente ya admite los tres niveles y se reutiliza.
9. Administrar la equivalencia con `centros_costo.catalogo.administrar`, motivo obligatorio, auditoría compartida, idempotencia y versión `If-Match`. No crear permisos ni parámetros con IDs fijos.

## Alcance del reemplazo de ADR-0050

Se reemplazan la obligatoriedad de Dim3 en RQ, su captura siempre manual y la afirmación de separación total respecto del departamento. La referencia a máquina obligatoria en RQ regularizadora también queda subordinada a P203. Se amplía la elección en OC manual a centros de departamento.

Permanecen la herencia documental, los permisos de captura por proxy, la lectura histórica «ver ≠ elegir», la regularización que no reescribe el gasto del vale y la ausencia de doble conteo. El flujo del vale urgente conserva su selector actual y sus reglas; este paquete no cambia ese origen de captura.

## Implementación y consecuencias

- Migración `ADM08EquivalenciaDepartamentoCentroCosto`: tabla de equivalencias con índice único empresa/sucursal/departamento, auditoría y concurrencia del `BaseDbContext`.
- Semilla opt-in en ambientes no productivos con `Seed:DatosDemo:Habilitado` o `Seed:DemoSesion:Habilitado`: sólo departamentos/sucursales del seed, label **DEMO · por validar con Laura (V49) · equivalencia ficticia, no aprobada**. No sustituye equivalencias existentes ni modifica el catálogo M1.
- Se reutilizan `CentroCostoId`, los puertos de elegibilidad/lectura y los componentes del sistema de diseño; sus nombres históricos `Dim3` no limitan el nivel del ID en estos contratos.
- Se completa una omisión del código anterior: las recepciones tenían el campo, pero no copiaban el centro de la OC. Ambas variantes de recepción ahora lo guardan y muestran.
- Cambiar una equivalencia afecta capturas posteriores; no actualiza en masa líneas históricas. No se transforma un centro superior en máquina ficticia.
- No se inventa un reporte de Fase F ausente en el checkout. Se verifica compatibilidad de identificación y jerarquía para los tres niveles; la aceptación de un reporte real y su salida queda **Por confirmar**.

## Verificación y pendientes

Pruebas de regla, adaptadores, requisitante, congelamiento, recepción/salida, lectura histórica y selector editable/solo lectura. Pruebas PostgreSQL de endpoints y P2 escritas, pendientes de `tools/validate-integration-isolated.sh` completo y evidencia rojo/verde por Claude. Ver [informe de entrega ADM08](../entregas/ADM08-ceco-heredado-departamento.md). Integración real, equivalencias definitivas V49, revisión visual en runtime y aceptación de Millet permanecen **Por confirmar**.
