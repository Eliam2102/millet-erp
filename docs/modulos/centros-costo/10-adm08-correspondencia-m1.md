# ADM-08 - Correspondencia de catalogo M1

**Fuente:** `Propuesta Cecos ERP_M1.xlsx`, recibido el 2026-09-29.
**Hojas:** `CeCo` y `NumEq`.
**Alcance:** comparacion contra `SiembraCatalogoSql` sin relacionar automaticamente CeCos con sucursales o departamentos del ERP.

## Lectura del modelo

| Nivel | Fuente M1 | Modelo ERP |
| --- | --- | --- |
| Ubicacion | `CV UBICACION` / `UBICACION` | Dim1 |
| Centro de costo | `CECO` / `DESCRIPCION DE CECO` | Dim2 |
| Equipo imputable | `CV Num.EQ.` / `AREA` | Dim3 |
| Clasificacion de CeCo | `CV AREA` / `AREA` en hoja CeCo | GrupoDim2 |
| Clasificacion de equipo | `PROCESO` | GrupoDim3 |

Los grupos clasifican nodos; no agregan otro padre a la jerarquia. El destino organizacional de esta tabla proviene de la fuente y no crea vinculos con los catalogos compartidos.

## Resumen de comparacion

| Elemento | Fuente M1 | Siembra vigente | Resultado |
| --- | ---: | ---: | --- |
| Dim1 / ubicaciones | 5 | 5 | Coinciden las claves 101-105 |
| Dim2 / CeCos | 49 | 57 | 33 claves presentes; 16 ausentes; 24 antiguas fuera de propuesta |
| Dim3 / NumEQ | 352 | 361 | 261 claves exactas; 90 diferencias de cero a la izquierda; 1 alta; 10 antiguas fuera de propuesta |

## Correspondencia de los 49 CeCos

| Clave M1 | Nombre M1 | CV area | Area | CV ubicacion | Ubicacion | Estado | Observacion |
| --- | --- | ---: | --- | ---: | --- | --- | --- |
| 20PDMC | CORTE | 20 | PRODUCCION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20PDCN | CNC | 20 | PRODUCCION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20PDFL | FLOTADO | 20 | PRODUCCION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20PDIN | INSULADO | 20 | PRODUCCION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20PDLA | LAMINADO | 20 | PRODUCCION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20PDSG | SERIGRAFIA | 20 | PRODUCCION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20PDTP | TEMPLADO | 20 | PRODUCCION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20PDCF | CONTROL y FILTRO | 20 | PRODUCCION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20DD00 | DIRECCION/GERENCIAS DE PRODUCCION | 21 | OPERACION | 101 | CONKAL | Nombre distinto | Actual: DIRECCION DE OPERACIONES. |
| 20PL00 | PLANEACION | 21 | OPERACION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20CL00 | CALIDAD | 21 | OPERACION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20MTGG | GERENCIA DE MANTENIMIENTO | 21 | OPERACION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20MTME | MANTENIMIENTO EQUIPO | 21 | OPERACION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20MTMI | MANTENIMIENTO INSTALACIONES | 21 | OPERACION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 20PDPR | SERVICIOS PERIFERICO | 21 | OPERACION | 101 | CONKAL | Nombre distinto | Actual normaliza el plural como SERVICIOS PERIFERICOS. |
| 20PDIF | INFRAESTRUCTURA | 21 | OPERACION | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 21LGGG | GERENCIA LOGISTICA | 22 | LOGISTICA NACIONAL | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20LGGG. |
| 21LGLL | LOGISTICA LOCAL | 22 | LOGISTICA NACIONAL | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20LGLL. |
| 21LGLN | LOGISTICA NACIONAL | 22 | LOGISTICA NACIONAL | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20LGLN. |
| 21LGLP | LOGISTICA PENINSULAR | 22 | LOGISTICA NACIONAL | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20LGLP. |
| 21LGTC | TALLER DE CARPINTERIA | 22 | LOGISTICA NACIONAL | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20LGTC. |
| 21LGTD | TALLER DE SOLDADURA | 22 | LOGISTICA NACIONAL | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20LGTD. |
| 21LGTM | TALLER MECANICO | 22 | LOGISTICA NACIONAL | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20LGTM. |
| 22LGLE | LOGISTICA EXPORTACION | 23 | LOGISTICA EXPORTACION | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20LGLE. |
| 23AL00 | ALMACEN | 24 | ABASTO | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20AL00. |
| 23CO00 | COMPRAS | 24 | ABASTO | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20CO00. |
| 30DD00 | DIRECCION COMERCIAL | 30 | COMERCIAL | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 30CC00 | CALL CENTER | 30 | COMERCIAL | 101 | CONKAL | Nombre distinto | Actual: CALL CENTER Q.ROO. |
| 30EX00 | EXPORTACION | 30 | COMERCIAL | 101 | CONKAL | Nombre distinto | Actual: VENTAS INTERNACIONALES. |
| 30OP00 | OBRAS Y PROYECTOS | 30 | COMERCIAL | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 30VM00 | SUCURSAL (CHICHI SUAREZ) | 30 | COMERCIAL | 102 | CHICHI SUAREZ | Nombre distinto | Actual agrega el prefijo VENTAS. |
| 30VM01 | SUCURSAL (CIRCUITO) | 30 | COMERCIAL | 103 | CIRCUITO | Nombre distinto | Actual agrega el prefijo VENTAS. |
| 30VM02 | SUCURSAL (PLANTA PINTURA) | 30 | COMERCIAL | 105 | PLANTA PINTURA | Nombre distinto | Actual agrega el prefijo VENTAS. |
| 30VT00 | SUCURSAL (CANCUN) | 30 | COMERCIAL | 104 | CANCUN | Nombre distinto | Actual agrega el prefijo VENTAS. |
| 30DD01 | MERCADOTECNIA | 30 | COMERCIAL | 101 | CONKAL | Renombre por confirmar | Candidato actual: 60DD00. |
| 40DD00 | ADMON Y FINANZAS | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Nombre distinto | Actual agrega el prefijo DIRECCION DE. |
| 40CB00 | CONTABILIDAD | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 40CC00 | CUENTAS POR COBRAR | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 40FS00 | FISCAL | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 40IG00 | INGRESOS | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Nombre distinto | Actual agrega la ubicacion CONKAL. |
| 40JR00 | JURIDICO | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 40PT00 | PATRIMONIAL | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 40TS00 | TESORERIA | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Coincide | Misma clave y jerarquia disponible. |
| 40DD01 | CAPITAL HUMANO | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Ambiguo | Candidatos actuales: 50DD00 y 50GC00. |
| 40NM00 | NOMINA | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Faltante | Sin equivalente inequivoco en la siembra. |
| 40SH00 | SEGURIDAD E HIGIENE | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Renombre por confirmar | Candidato actual: 50SH00. |
| 40SI00 | SERVICIOS GENERALES | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Renombre por confirmar | Candidato actual: 50SI00. |
| 40TI00 | TI (TECNOLOGIA DE LA INFORMACION | 40 | ADMINISTRACION Y FINANZAS | 101 | CONKAL | Renombre por confirmar | Candidato actual: 20TI00. |
| 50DD00 | CORPORATIVO MILLET | 50 | CM | 101 | CONKAL | Conflicto | La clave actual pertenece a Capital Humano; el candidato semantico es 70DD00. |

## Diferencias NumEQ

- 261 claves coinciden exactamente.
- 90 claves de vehiculos difieren solo por relleno numerico, por ejemplo `VD001` frente a `VD0001`.
- `CCTER01` aparece en M1 y no existe en la siembra.
- Diez Dim3 actuales no aparecen en M1: `ADCHS01`, `ADCIR01`, `ADCUN01`, `CHGCP01`, `CONAC01`, `COQRO01`, `COYUC01`, `GPGRA01`, `IMGRA01` y `MKPPU01`.

## Decisiones que requieren confirmacion

1. Confirmar si las claves candidatas son renombres o altas nuevas con baja logica de la anterior.
2. Resolver el conflicto de `50DD00` y la consolidacion de `50DD00`/`50GC00` en `40DD01`.
3. Confirmar si los nueve CeCos antiguos restantes y las diez NumEQ ausentes deben quedar inactivos.
4. Confirmar si los nombres de M1 sustituyen las descripciones normalizadas actuales.
5. Confirmar vigencia real y combinaciones permitidas. La fuente no contiene fechas ni estado.

Hasta recibir estas confirmaciones, el cierre tecnico puede probar CRUD, jerarquia, alcance e historico, pero no debe declarar aprobados los datos reales por Contabilidad/Millet.
