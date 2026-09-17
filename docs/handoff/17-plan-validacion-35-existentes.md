# Plan ejecutable de validación · 35 funcionalidades heredadas como existentes

## Propósito

Esta guía convierte la auditoría de código en trabajo ejecutable. La marca histórica `Ya existe y funciona` no se conserva como aceptación: cada función debe demostrar pantalla o interfaz, caso positivo, caso negativo relevante, persistencia/efecto, regresión y evidencia. La aceptación de Millet se registra después, en UAT.

La fuente de clasificación es `10-auditoria-35-existentes.md`. El inventario operativo y los responsables provienen de `inventario-funcional-fase1.csv`.

## Evidencia técnica renovada

- **Corte:** 16 de septiembre de 2026
- **Commit ejecutado:** `e6f506210bb1a345d7bde0d575d63df11151ee84`

- Backend: compilación completa, 0 advertencias y 0 errores.
- Backend unitario: 2,279 pruebas aprobadas.
- Backend integración: 489 pruebas aprobadas en tres bases aisladas — API 362, Compras 120 y A+W 7.
- Frontend: 278 archivos y 1,567 pruebas aprobadas.
- Frontend: compilación de producción aprobada.
- Migraciones: 12 contextos aplicados desde cero a una base exclusiva.

Esta evidencia acredita el gate automatizado del repositorio. No acredita por sí sola el recorrido individual de las 35 funciones, una integración externa real ni UAT.

## Estados permitidos

1. `Automatización verde`: compila y las suites relacionadas no fallan.
2. `Recorrido local aprobado`: se ejecutaron caso positivo y negativo con datos ficticios y evidencia.
3. `Lista para UAT`: el recorrido local está aprobado y no falta una integración o dato real.
4. `UAT aprobada`: el responsable funcional de Millet ejecutó/aceptó el caso y quedó evidencia.
5. `Reclasificada`: se comprobó que era parcial o no existía; se corrigieron matriz, tarea y estimación.

No se permite saltar de `Automatización verde` a `UAT aprobada`.

## Evidencia mínima por función

Cada ejecución debe adjuntar:

- ID funcional, commit y ambiente.
- URL/ruta o endpoint utilizado.
- datos de entrada sin información productiva;
- captura o log del caso positivo;
- captura o log del caso negativo;
- identificador del registro creado/modificado y efecto persistido;
- prueba automatizada asociada y resultado;
- regresión ejecutada;
- desviación encontrada, si existe;
- resultado: `Aprobada local`, `Parcial`, `No localizada`, `Bloqueada` o `Lista para UAT`.

## Orden de ejecución

### Bloque 1 · Ola 1A

Se ejecuta antes o junto con el arranque de Fundaciones. Estas cuatro funciones no pueden conservar cero esfuerzo operativo.

| ID | Responsable | Estado técnico | Recorrido obligatorio | Bloqueo de cierre |
|---|---|---|---|---|
| F1-ADM-01 | Geovany | Verificable local | Crear/editar estructura de dos empresas; demostrar separación y rechazo de mezcla | Estructura vigente de empresas, sucursales y áreas de Millet para UAT |
| F1-ADM-03 | Geovany | Verificable local | Ejecutar alta, cambio, autorización y acceso; comprobar usuario, fecha y antes/después | Casos críticos y responsables de auditoría para UAT |
| F1-ADM-10 | Geovany | Verificable local | Entrar con al menos dos perfiles ficticios y probar accesos permitidos/negados | Matriz final de permisos de Millet |
| F1-ADM-11 | Geovany | Parcial/condicionado | Probar adjunto, consulta, metadatos y permiso en cada módulo soportado; listar módulos no cubiertos | Decisión interna sobre registros y módulos cubiertos por adjuntos |

Salida del bloque: cuatro expedientes de evidencia y corrección de ADM-11 en matriz/tarea. Ninguna queda UAT aprobada sin Millet.

### Bloque 2 · Ola 1B-1

| ID | Responsable | Estado técnico | Recorrido obligatorio | Cierre o reclasificación |
|---|---|---|---|---|
| F1-COM-01 | Uzziel | Verificable local | Consultar requisición, detalle, autorizaciones y saldo por comprar | Conciliar con caso representativo de Compras |
| F1-COM-02 | Uzziel | No localizado | Confirmar ausencia de entidad/pantalla dedicada y abrir tarea de construcción | Reclasificar; definir criterio y estimación, no intentar “validarla” |
| F1-COM-03 | Uzziel | Verificable local | Crear OC desde requisición con saldo y rechazar excedente | Evidencia de cubrimiento y saldo restante |
| F1-COM-04 | Uzziel | Verificable local | Crear OC sin requisición, exigir motivo/autorización y negar liberación incompleta | Evidencia del flujo de excepción |
| F1-COM-05 | Uzziel | Verificable local | Autorizar montos con y sin segunda firma | Matriz de montos y autorizadores vigente para UAT |
| F1-COM-10 | Uzziel | Parcial/condicionado | Generar y consultar PDF; separar envío y acuse de correo no acreditados | Reclasificar envío/acuse como trabajo pendiente |
| F1-ALM-01 | Geovany | Verificable local | Alta/edición/baja de almacén, ubicación y asignación; negar uso inactivo | Catálogos vigentes para UAT |
| F1-ALM-02 | Geovany | Verificable local | Recorrer OC–recepción–factura y comprobar existencias | Caso representativo del cliente para UAT |
| F1-ALM-03 | Geovany | Verificable local | Recibir packing list y asociar factura después sin duplicar inventario | Muestra/contrato aplicable para cierre real |
| F1-ALM-06 | Geovany | Verificable local | Registrar salida nominal y rechazar saldo autorizado insuficiente | Caso real posterior para UAT |
| F1-ALM-08 | Geovany | Verificable local | Registrar devolución interna/proveedor y comprobar reversa y origen | Política vigente para UAT |
| F1-ALM-09 | Geovany | Verificable local | Conteo con diferencia, autorización y movimiento de ajuste | Responsables y tolerancias para UAT |
| F1-ALM-10 | Geovany | Verificable local | Configurar mínimos/máximos, simular consumo y revisar cálculo | Parámetros y consumo representativo del cliente |
| F1-MP-04 | Geovany | Parcial/condicionado | Ejecutar recepción reutilizada y documentar lo que no cubre de materia prima/A+W | Contrato A+W y reglas de material directo |

### Bloque 3 · Ola 1B-2

| ID | Responsable | Estado técnico | Recorrido obligatorio | Cierre o reclasificación |
|---|---|---|---|---|
| F1-CXP-03 | Uzziel | Verificable local | Registrar servicio/gasto sin OC, clasificar, autorizar y comprobar pasivo | Catálogos/autoridades reales para UAT |
| F1-CXP-04 | Uzziel | Verificable local | Registrar gasto aduanal, ligar expediente y probar distribución | Regla y expediente representativo de Comercio Exterior |
| F1-CXP-06 | Uzziel | No localizado | Confirmar que reportes/evento no equivalen a propuesta de pagos | Decidir dueño CxP/Tesorería, construir y reestimar |
| F1-CXP-07 | Uzziel | Verificable local | Aplicar anticipo/NC parcial y total; negar saldo negativo | Caso representativo y reglas contables para UAT |

### Bloque 4 · Ola 1C

| ID | Responsable | Estado técnico | Recorrido obligatorio | Cierre o reclasificación |
|---|---|---|---|---|
| F1-FAC-02 | Geovany | Parcial/condicionado | Ejecutar construcción local del CFDI; separar timbrado real | Sandbox, certificados y parámetros fiscales de Millet |
| F1-FAC-04 | Geovany | Verificable local | Probar pedido con y sin ranura y registrar la regla aplicada | Caso A+W acordado para UAT |
| F1-FAC-05 | Geovany | Verificable local | Facturar contado MXN; negar moneda y forma inválidas | Formas vigentes para UAT |
| F1-FAC-06 | Geovany | Parcial/condicionado | Probar cálculo de REPP local y evento desde pago | PAC/sandbox para UUID real |
| F1-FAC-07 | Geovany | Parcial/condicionado | Construir complemento CCE con datos ficticios y validar campos | Catálogos, caso real, certificado y PAC |
| F1-CXC-01 | Uzziel | Verificable local | Consultar cartera/antigüedad/estado de cuenta y conciliar totales | Saldo real controlado para UAT |
| F1-CXC-02 | Uzziel | Parcial/condicionado | Recorrer cobro–aplicación–remanente entre módulos | Integración local completa y caso de negocio |
| F1-CXC-03 | Uzziel | Parcial/condicionado | Probar prioridad y conservar depósito no identificado | Regla definitiva y recorrido extremo a extremo |
| F1-CXC-05 | Uzziel | Parcial/condicionado | Probar anticipos, NC y compensaciones con límites | Flujo unificado aún no acreditado; reclasificar si falta |

### Bloque 5 · Ola 1D

| ID | Responsable | Estado técnico | Recorrido obligatorio | Cierre o reclasificación |
|---|---|---|---|---|
| F1-TES-01 | Uzziel | Verificable local | Administrar cuentas de dos empresas y negar cuenta ajena/inactiva | Catálogo bancario controlado para UAT |
| F1-TES-02 | Uzziel | Verificable local | Registrar movimiento, depósito y transferencia; comprobar saldo/referencia | Datos bancarios representativos para UAT |
| F1-TES-03 | Uzziel | Verificable local | Pago parcial/total contra pasivos elegibles y evento confirmado | Caso real controlado para UAT |
| F1-TES-08 | Uzziel | Parcial/condicionado | Registrar y validar REPP recibido del proveedor | Corregir texto: Millet no emite el REPP del proveedor |

## Regla para ClickUp y Notion

- Cada ID conserva su ficha funcional; la validación se registra como subtarea o actividad de QA, no como una segunda funcionalidad.
- `F1-COM-02` y `F1-CXP-06` deben pasar a trabajo de construcción/reestimación.
- Los diez parciales deben describir exactamente qué se reutiliza y qué falta; no usar `Ya existe y funciona`.
- El enlace a evidencia debe vivir en la ficha/tarea correspondiente.
- `Resultado QA/UAT` sólo cambia cuando exista evidencia individual, no por el gate general de 4,335 pruebas.

## Criterio de cierre de este frente

El frente termina cuando las 35 filas tienen resultado individual y evidencia enlazada:

- 23 recorridos locales ejecutados o reclasificados;
- 10 parciales corregidos y con trabajo faltante identificado;
- 2 no localizadas convertidas en tareas de construcción con estimación;
- regresión registrada;
- UAT marcada únicamente donde Millet la haya ejecutado y aceptado.
