# ADM-08 - Impacto historico de la reconciliacion M1

## Decision aprobada

El catalogo del archivo `Propuesta Cecos ERP_M1.xlsx` es la fuente de verdad vigente. Se conservan los registros adicionales del ERP, se crean los CeCos faltantes y las equivalencias inequivocas adoptan clave, nombre, grupo y jerarquia de M1.

## Estrategia de conservacion

| Caso | Tratamiento | Impacto historico |
| --- | --- | --- |
| Cambio de clave, nombre o grupo del mismo CeCo | Actualizar la fila existente conservando su `id` | Los documentos siguen apuntando al mismo registro. Al consultar el catalogo actual muestran la clave o descripcion vigente. |
| CeCo `40NM00` inexistente | Crear una fila nueva | No modifica documentos anteriores. |
| Registro adicional que no aparece en M1 | Conservar sin cambios | Sus referencias siguen siendo validas; no se ofrece como dato M1 nuevo, pero tampoco se pierde. |
| Equipo que cambia de padre | Desactivar la fila anterior, marcar su clave como historica y crear una fila activa con la clave M1 bajo el padre nuevo | Los documentos anteriores conservan el `id` y padre originales; las operaciones nuevas seleccionan la fila vigente. |
| Equipo nuevo `CCTER01` | Crear una fila nueva | No modifica documentos anteriores. |
| Claves vehiculares con cero adicional | Actualizar la fila conservando su `id` | Las referencias permanecen; las consultas muestran el formato M1. |

## Colision `50DD00`

La siembra anterior usa `50DD00` para Capital Humano, mientras M1 usa esa clave para Corporativo Millet y define Capital Humano como `40DD01`. La migracion realiza un intercambio controlado:

1. Reserva temporalmente la clave de la fila actual de Capital Humano.
2. Actualiza Corporativo Millet de `70DD00` a `50DD00`, conservando su `id`.
3. Actualiza Capital Humano a `40DD01`, conservando su `id`.

No se elimina ninguna de las dos filas y las referencias historicas conservan sus identificadores.

## Equipos con cambio de padre

| Clave M1 | Padre historico | Padre vigente M1 | Riesgo evitado |
| --- | --- | --- | --- |
| `VU056` | `20PDGG` | `20DD00` | No atribuir documentos antiguos de Produccion a Direccion de Produccion. |
| `CHDIR01` | `40DD01` anterior | `50DD00` | No reinterpretar referencias antiguas durante el intercambio de CeCos. |
| `VU106` | `40DD01` anterior | `50DD00` | Mantener la imputacion original de documentos existentes. |
| `VV060` | `50GC00` | `40DD01` | No mover retroactivamente consumos de Gerencia de Capital Humano. |

## Riesgos residuales

- Reportes que guardan solamente la clave como texto, en vez del `id`, pueden requerir una tabla de equivalencias para consultas historicas.
- Integraciones externas que tengan claves antiguas en cache deben actualizarse al catalogo M1.
- La baja y alta de los cuatro equipos produce dos IDs deliberadamente distintos: uno historico y uno vigente.
- El rollback automatico no es seguro después de que documentos nuevos utilicen las claves M1; una reversa debe realizarse con conciliacion de referencias.

## Verificacion requerida

- Confirmar que las 49 claves CeCo de M1 existen activas tras la migracion.
- Confirmar que las claves antiguas equivalentes ya no se ofrecen para operaciones nuevas.
- Confirmar que los cuatro IDs historicos permanecen consultables e inactivos.
- Confirmar que los cuatro IDs nuevos aparecen bajo el padre M1.
- Confirmar que los registros adicionales del ERP siguen presentes.
- Ejecutar pruebas de selector, permisos, concurrencia y lectura historica.
