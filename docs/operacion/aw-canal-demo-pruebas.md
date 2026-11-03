# Canal demo de A+W en el servidor de pruebas

Objetivo: que Millet pueda, en el servidor de pruebas, cambiar el origen de A+W a **Demo**, probar clientes,
productos y pedidos con datos sintéticos, y volver a **Real** con un clic. No toca A+W real ni Azure fuera de lo
que se lista aquí. Tarea ClickUp 86e3uzwme.

## Qué es

- El selector de origen ya existe en el ERP (`GET/PUT /api/v1/integraciones/aw/origen`; pantalla
  Datos maestros → Sincronización A+W, pestañas Real / Demo). Requiere el permiso
  `IntegracionesAwAdministracionConfiguracion`; cada cambio queda auditado.
- El origen Demo lee una base PostgreSQL propia (`millet_aw_origen`) con 62 clientes, 47 productos y 3 pedidos
  (900101, 900102 y 900103). El 900103 trae un cliente inexistente a propósito.
- Es una segunda base de datos, no una segunda copia del ERP: la base del ERP es la misma para Real y Demo.

## Qué crea Eliam (el equipo de desarrollo no crea nada en Azure)

1. **Base y rol dedicado** en el servidor PostgreSQL de pruebas que ya existe (no hay servidor nuevo):

   ```sql
   -- conectado como el administrador del servidor, base postgres
   CREATE ROLE aw_origen_app LOGIN PASSWORD '<contraseña-larga-generada>';
   GRANT aw_origen_app TO pgadmin;           -- requisito de Azure para CREATE DATABASE ... OWNER
   CREATE DATABASE millet_aw_origen OWNER aw_origen_app;
   REVOKE ALL ON DATABASE millet_aw_origen FROM PUBLIC;
   ```

   El rol solo es dueño de esa base; no recibe permisos sobre `millet_erp` ni sobre ninguna otra.

2. **Secreto en el Key Vault del servidor de pruebas** (se crea a mano, no lo crea Bicep):

   ```bash
   az keyvault secret set --vault-name <kv-de-pruebas> --name aw-origen-pg-connection-string \
     --value 'Host=<servidor>.postgres.database.azure.com;Database=millet_aw_origen;Username=aw_origen_app;Password=<contraseña>;SSL Mode=Require'
   ```

   Mientras el secreto no exista, la referencia queda sin resolver y el selector responde
   `AW_DEMO_NO_CONFIGURADA` (la pestaña Demo sigue deshabilitada). No rompe el arranque.

3. **Valores del servidor de pruebas**, ya puestos en `infra/parameters/qa.bicepparam`:

   ```bicep
   param awOrigenDemoPermitido = true
   param awClientesLecturaHabilitada = true
   param awClientesAplicacionHabilitada = true
   param awProductosOrigenHabilitado = true
   param awSolicitudesEmpresaId = '<GUID de la empresa de pruebas>'   // sin esto los pedidos no se ingestan
   ```

   En Bicep los cuatro `bool` valen `false` por omisión, así que dev y producción no cambian. El ambiente de
   pruebas debe correr con `ASPNETCORE_ENVIRONMENT` distinto de `Production` (`Staging` o `Development`); en
   `Production` el selector Demo se bloquea aunque todo lo demás esté puesto.

4. **Reiniciar el App Service** después de desplegar la infraestructura y de crear el secreto (las referencias a
   Key Vault se resuelven al arrancar).

5. **Catálogos del ERP de pruebas** (una vez, desde la UI de Datos maestros): la sucursal que recibirá el pedido
   900101 con clave A+W `CIRCUITO` y la del 900102 con `CANCUN`. Los canales «Ventas Circuito» y «Ventas Cancun» ya
   vienen sembrados. Sin esas claves los pedidos quedan pospuestos.

## Cargar los datos de demo

Desde una máquina con `psql` y acceso al servidor (regla de firewall temporal para esa IP, o Azure Cloud Shell;
quitar la regla al terminar):

```bash
cd tools/aw-origen-demo
PGHOST=<servidor>.postgres.database.azure.com PGUSER=aw_origen_app PGPASSWORD='<contraseña>' ./aw-origen-demo.sh up
```

- Con `PGHOST` definido el script usa `psql` directo (`PGPORT` por omisión 5432, `PGSSLMODE` por omisión `require`)
  en lugar de `docker exec`. Sin `PGHOST` funciona como antes contra el contenedor local.
- `up` es idempotente: crea la base solo si no existe y reaplica esquema y semillas.
- `reseed` deshace cambios hechos a mano a clientes/productos; `psql -c "..."` permite editar un dato para ver la
  sincronización.

Costo: ≈ $0 (una base más en el servidor existente; el almacenamiento son unos cientos de KB). Un servidor B1ms
nuevo costaría ~$15–25 al mes y no hace falta.

## Cómo cambia Millet de Demo a Real

1. Datos maestros → Sincronización A+W → pestaña **Demo**. Confirmar el cambio.
2. Ejecutar la sincronización: se leen 62 clientes y 47 productos. Esperado: 61 clientes y 45 productos creados;
   hay filas inválidas a propósito (aparecen como errores por referencia) para probar la corrección.
3. Pedidos: en un minuto aproximado el worker ingesta 900101 y 900102 (estado Importado en Pedidos facturables) y
   el 900103 cae a la bandeja de excepciones con motivo `ClienteNoExiste`.
4. Para volver, pestaña **Real**. El cambio es inmediato y queda auditado (quién y cuándo).

Por API (mismo permiso): `PUT /api/v1/integraciones/aw/origen` con `{"origen":"Demo"|"Real"}`, cabeceras
`If-Match: "<versión>"` e `Idempotency-Key`.

## Entrada de pieza terminada a Producto Terminado (PT-01)

El origen Demo incluye las tablas de escaneos de pieza terminada (`dbo.awbar_bearbeit`, `dbo.pool_pos`,
`dbo.pool_bearbeit` y, solo en la demo, `dbo.or_avbereich` como sustituto de `OR_AVBEREICH` de MILMAIN). El ERP
solo las lee; nunca escribe en ellas.

1. Cargar los escaneos de muestra: `tools/aw-origen-demo/aw-origen-demo.sh piezas` (también corre al final de `up`;
   repetirlo es inocuo). Incluye el pedido 900101 (CIRCUITO) con piezas, una reposición, una pieza que excede la
   cantidad, un escaneo sin terminar y uno en estación fuera de lista; el 900199 (no está en Facturación) y el
   900102 (CANCUN).
2. Habilitar la entrada con variables de entorno:
   `IntegracionesAw__PiezasPt__EntradaHabilitada=true`, `IntegracionesAw__PiezasPt__EmpresaId=<guid>` e
   `IntegracionesAw__OrigenDemo__Permitido=true`.
3. La lista de estaciones de pieza terminada es configuración, no código: clave
   `IntegracionesAw:PiezasPt:EstacionesPt` (rangos `"2821-2831"` o números `"2840"`). Con la lista vacía no se
   admite ningún escaneo. La lista es la de la consulta de referencia de TI (27-oct): 2821–2831, 2840, 3201–3315 y
   3501–3509. La muestra trae un escaneo en la 2835 para comprobar que se ignora.
4. El origen Real requiere `ContratoI134Firmado`, `EntradaHabilitada` y `ConnectionStrings:AwClientesDb` (la conexión de
   clientes, login `millet_erp_lector_aw`; sin cadena ni conexión híbrida propias). Sigue apagado por configuración.
   Jorge (29-oct, SRC-114) dejó el script 15 corrido y el servidor vinculado `AWPROD_ERP`:
   - El lector consulta `[AWPROD_ERP].alcimdb.dbo.…` con `OPENQUERY`, para que el filtro se resuelva en producción.
   - Pieza terminada = `fertig = 1 AND bruch_flag = 0 AND LEN(etikettnr) = 9` (la rotura entra como etiqueta + `01` con
     `bruch_flag = 1` y no cuenta). La muestra demo incluye una rotura para comprobarlo.
   - La sucursal no se lee de AWPRODUCTION: la pieza toma la del pedido, que ya viene de MILMAIN `OR_AVBEREICH`.

## Riesgos

- **Los datos maestros de demo se quedan al volver a Real.** Clientes y productos sincronizados desde Demo se
  guardan en la misma base del ERP; cambiar a Real no los borra.
- **Los pedidos de demo se ingestan una sola vez** por base del ERP (el ERP recuerda los ya procesados). Para
  repetir la prueba hay que usar números de pedido nuevos en `aw_solicitud_pedido`.
- La contraseña del rol vive solo en el Key Vault; no va en parámetros ni en el repositorio.

## Prueba local equivalente (evidencia)

Se probó el mismo flujo en local con `ASPNETCORE_ENVIRONMENT=Staging`, base de pruebas del ERP y
`millet_aw_origen` en el Postgres de pruebas, con sesión real de Entra:
`GET /origen` → `demoConfigurada:true` y los tres candados en `true`; `PUT` a Demo; sincronización (61 clientes y 45
productos creados); pedidos 900101 y 900102 Importados; 900103 en excepciones; `PUT` de vuelta a Real. Salidas en
`Obsidian/Millet-ERP/evidencias/86e3uzwme/`.

## Guion de prueba para el dueño (Eduardo)

1. Entrar al servidor de pruebas con un usuario con permiso de configuración de A+W.
2. Datos maestros → Sincronización A+W. Debe verse la pestaña **Demo** habilitada. Si está gris, falta el secreto
   o algún valor de la sección «Qué crea Eliam».
3. Elegir **Demo**. Debe aparecer quién y cuándo hizo el cambio.
4. Ejecutar la sincronización. Revisar que haya unos 61 clientes y 45 productos nuevos, y que los errores
   mencionen una referencia concreta (cliente 58, productos 46 y 47).
5. Abrir Facturación → Pedidos facturables. En unos minutos deben aparecer 900101 y 900102 como Importado.
6. Abrir la bandeja de excepciones de importación. Debe estar el pedido 900103 con «cliente no existe».
7. Volver a **Real**. La etiqueta del origen debe cambiar al instante.
