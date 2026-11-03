# Carga de maestros desde el ERP

Los listados de clientes, artículos, proveedores, productos A+W y la configuración
de centros de costo ofrecen **Cargar desde Excel** a quienes tienen el permiso
de carga correspondiente. Para centros de costo, se carga cada dimensión por separado:
primero los padres y grupos, después sus hijos.

1. Descargar la plantilla Excel y consultar su hoja **Instrucciones**. También se
   puede descargar CSV. Artículos y proveedores aceptan los encabezados originales
   de `carga-sap-scripts/01_extract_*.sql` sin renombrarlos.
2. Elegir un archivo `.xlsx` o `.csv` de hasta 10 MB y 20,000 registros. El CSV puede
   usar UTF-8, BOM UTF-8 o Windows-1252, con coma o punto y coma.
3. Revisar nuevos, actualizaciones, registros sin cambios y errores. La previa sólo
   consulta: todavía no incorpora registros ni produce auditoría de cambios.
4. Confirmar aplica los registros válidos en una transacción. Los errores se omiten
   y se pueden descargar en Excel, corregir y volver a cargar. Las columnas añadidas
   **Error** y **Renglón original** se ignoran al volver a leer ese archivo.

Las celdas vacías conservan valores existentes. Las claves de correlación de A+W
y el origen de clientes no cambian. No se cargan cuentas bancarias ni precios.
El catálogo de unidades, categorías y clasificaciones debe existir previamente;
las referencias sin coincidencia se reportan en el renglón correspondiente.

La transformación de proveedores sigue el script de **pruebas**:
clave `P######` continuando el máximo existente, `ClaveLegacy = CardCode`,
`MXP → MXN`, `## → sin moneda preferida`, tipo de persona derivado del RFC y
altas activas. Una clave explícita en la plantilla sustituye la generación del
consecutivo. Las altas guardan `OrigenCarga = "CargaSap"`, que no cambia al editar.
La carga incorpora datos y clasificación fiscal, pero no documentos: el proveedor
activo muestra **Expediente incompleto** hasta cubrir los obligatorios aplicables
(o sus excepciones). La advertencia no bloquea su uso en OC; el listado permite
filtrar **Sólo expediente incompleto**. Los documentos vencidos también cuentan
como pendientes. El alta individual del ERP conserva su flujo de revisión habitual.
La variante de claves por prefijo del runbook de producción no se activa con esta carga.

Para artículos, la vista previa (sin insertar) incluye un **Resumen**: totales por
grupo SAP y por tipo (Servicio/Estándar), duplicados en el archivo, unidades sin
equivalencia (p. ej. CAJA, PAQ, ROLLO) y posibles A+W (grupos 142–147). Ese resumen
es el reporte de la prueba en seco; sustituye la corrida manual de
`02_load_articulos_local.sql` en servidores de pruebas. Repetir la carga confirmada
da 0 nuevos. Corrida: Datos maestros → Artículos → Cargar desde Excel, con el CSV
de `01_extract_articulos.sql`; ver el resumen, confirmar y adjuntar el resumen a la tarea.

Los artículos conservan `ItemCode` como clave y clave legacy; `invnt_item = N`
representa Servicio. Las referencias de productos o componentes A+W y los grupos
SAP 142–147 se reportan para revisión y no se incorporan por este flujo.

La carga fiscal A+W admite reglas y excepciones con los mismos campos fiscales
del importador anterior. Las excepciones requieren un producto existente. El
endpoint anterior con cuerpo JSON `{ "csv": "..." }` conserva su contrato y
utiliza el mismo motor; la pantalla nueva añade la revisión previa y Excel.

## API y permisos

Bajo `/api/v1/datos-maestros/cargas/{catalogo}`:

- `GET plantilla.xlsx` y `GET plantilla.csv`.
- `POST previa`: formulario multipart con `archivo`.
- `POST confirmar`: mismo archivo, campo `huella` de la previa e `Idempotency-Key`.

Catálogos: `clientes`, `articulos`, `proveedores`, `productos-aw-fiscal`,
`centros-costo-dim1`, `centros-costo-dim2` y `centros-costo-dim3`.
Permisos: `datos_maestros.<catalogo>.cargar`, excepto las dimensiones, que usan
`centros_costo.catalogo.cargar`. El permiso adicional para modificar fiscales
de clientes de A+W sigue aplicándose.

La huella SHA256 vincula la confirmación con el archivo revisado. Confirmar vuelve
a validar contra el estado actual; repetir la carga deja sin cambios los datos
ya aplicados. Para reintentar la misma petición se conservan la llave y el cuerpo
multipart; si la respuesta supera el límite de caché de idempotencia de la
plataforma, se debe generar otra previa y confirmar con una llave nueva.

## Migración necesaria

`CargaMaestrosPermisos` sólo inserta los cinco permisos nuevos en `identidad` y
los asigna a los roles que administran sus catálogos. Es necesaria porque el
catálogo canónico usa `HasData`: cambiar constantes no incorpora permisos en
bases ya existentes.

`ProveedorOrigenCarga` añade únicamente la columna nullable `origen_carga`
(`varchar(20)`) a `compartido.proveedores`. No reconstruye el origen de altas
anteriores: quedan sin origen de carga confirmado. Debe aplicarse antes de ejecutar
la versión que lee y escribe esta columna.
El bootstrap de administración y el snapshot de identidad quedan sincronizados.
