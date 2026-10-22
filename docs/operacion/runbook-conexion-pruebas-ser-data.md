# Conexión del servidor de pruebas con A+W en SER-DATA

Guía para TI de Millet industria de vidrio. Acompaña al documento «Comunicación entre A+W y el ERP».

El servidor de pruebas del ERP vive en Azure. SER-DATA (192.168.1.8, instancia `SER-DATA\AWBUSINESS`) está en la red interna de Millet, así que Azure llega a él por **Hybrid Connection**: el Hybrid Connection Manager (HCM) instalado en SER-DATA abre una conexión **saliente** por el puerto 443 hacia Azure. No se abre ningún puerto de entrada en la red de Millet.

| Conexión | Destino que usa el HCM en SER-DATA | Para qué |
|---|---|---|
| `hc-aw-business-sql` | `aw-sql-onprem:1433` | El ERP lee la tabla de avisos, las vistas de pedidos y los datos de clientes y productos |
| `hc-aw-drop-service` | `SER-DATA:5000` | El ERP deja los archivos de cotización y recoge PDF y resultados (servicio `MilletAwDropService`) |

`aw-sql-onprem` es un nombre que solo existe en el archivo `hosts` de SER-DATA y apunta a `127.0.0.1`; el certificado de SQL Server de SER-DATA lo incluye, por eso la conexión cifrada se puede verificar.

## 1. Quién hace qué

| Quién | Qué |
|---|---|
| TI de Millet | Las secciones 2 a 4 |
| Eliam (VILO) | Pasa a TI las dos cadenas de conexión del HCM; guarda en el Key Vault del ERP las contraseñas que defina TI |
| VILO | Enciende en el servidor de pruebas, uno por uno, clientes, productos, pedidos y retorno |

Ningún paso modifica datos de `MILMAIN`. El ERP solo lee de `MILMAIN`; los scripts 04 y 09 únicamente agregan usuarios de lectura y sus permisos. El ERP escribe solo en `MILLET_INTEGRACION_DEV`: anota el resultado de cada aviso y, en pruebas, también puede agregar y borrar avisos de prueba.

## 2. Prerrequisitos en SER-DATA

- [ ] El **SQL Server Agent** de `AWBUSINESS` está en ejecución y con inicio automático (la tarea de aviso es un job del Agent).
- [ ] Salida HTTPS 443 permitida hacia `*.servicebus.windows.net`.
- [ ] El archivo `C:\Windows\System32\drivers\etc\hosts` tiene la línea `127.0.0.1 aw-sql-onprem` (si no está, agregarla).

## 3. Scripts SQL

Se corren como **sysadmin**. Todos se pueden repetir sin daño. Para pruebas:

| # | Script | Qué hace | Ajuste para pruebas |
|---|---|---|---|
| 1 | `09_create_login_lector.sql` | Crea el usuario `millet_erp_lector_aw`, solo con lectura (`SELECT`) sobre las 9 tablas de clientes y productos que lee el ERP en `MILMAIN`. Pide la contraseña con `:setvar Password` (modo SQLCMD) | Ninguno |
| 2 | `07_job_aviso_pedidos.sql` | Crea la tarea `Millet ERP - aviso de pedidos A+W (DEV)`: cada 15 segundos escribe en la tabla de avisos los pedidos que llegan a 15 o 42 y las cancelaciones (900/999). No repite lo que ya escribió el botón del vendedor y no avisa los pedidos internos de la Serie 9000. Lee `MILMAIN` con prioridad baja y sin bloquearla | Cambiar `USE MILLET_INTEGRACION;` por `USE MILLET_INTEGRACION_DEV;` al inicio y `@base` (sección 4 del script) a `N'MILLET_INTEGRACION_DEV'`. El nombre del job agrega « (DEV)» solo |

No correr `05_seed_dev.sql`, `07_create_otr_tables.sql` ni `08_grant_otr.sql`.

La contraseña del script 09 la define TI y se la pasa a Eliam por un canal seguro para el Key Vault. No se escribe en archivos, correos ni chats.

## 4. Hybrid Connection Manager

1. Si el HCM no está instalado en SER-DATA, descargarlo de Microsoft (*Hybrid Connection Manager*) e instalarlo.
2. En el HCM: **Add a new Hybrid Connection → Enter Manually** y pegar la cadena de `hc-aw-business-sql` del servidor de pruebas que pasa Eliam. Repetir con `hc-aw-drop-service`.
3. Las dos deben quedar en **Connected** en menos de un minuto.

Si el HCM ya tiene conexiones de otro ambiente, se dejan como están; las de pruebas se agregan aparte.

## 5. Cifrado

La conexión con SQL Server va cifrada de punta a punta y **verificada**: el ERP exige `Encrypt=True` y no acepta certificados sin validar. Confía únicamente en el certificado actual de SER-DATA, cuya copia pública queda guardada en el Key Vault del ERP. TI no tiene que hacer nada para esto.

Lo comprobamos el 22 de octubre de 2026 desde Linux: TLS 1.2 (`ECDHE-RSA-AES256-SHA384`) con el nombre `aw-sql-onprem` verificado. **Si se renueva o cambia el certificado de SQL Server en SER-DATA, avisar a VILO** para cargar el nuevo en el Key Vault; si no, el ERP deja de conectarse (por diseño).

## 6. Cómo se comprueba que conecta

| Prueba | Dónde | Resultado esperado |
|---|---|---|
| HCM | SER-DATA | Las dos conexiones de pruebas en *Connected* |
| Tarea de aviso | SSMS → SQL Server Agent → Jobs | `Millet ERP - aviso de pedidos A+W (DEV)` con ejecuciones correctas cada 15 s |
| Servicio de archivos `MilletAwDropService` | SER-DATA: `Invoke-WebRequest http://localhost:5000/healthz` | `200` |
| Conexión cifrada del ERP | SSMS, como sysadmin: `SELECT c.client_net_address, c.encrypt_option, s.login_name FROM sys.dm_exec_connections c JOIN sys.dm_exec_sessions s ON s.session_id = c.session_id WHERE s.login_name LIKE 'millet_erp_%'` | `127.0.0.1` y `TRUE` |
| Pedidos | ERP de pruebas → Facturación | Un pedido que pasa a 15 o 42 aparece en menos de 1 minuto |

En pruebas, el aviso automático escribe en `MILLET_INTEGRACION_DEV`, y el botón del vendedor no apunta a esa base (la base de pruebas la llena solo el ERP). Por eso, que botón y aviso automático no se dupliquen se comprobó en nuestro ambiente de ensayo (botón antes del firme, botón después del firme, reconfirmación, cancelación y Serie 9000) y se confirma en producción cuando los dos escriban en la misma base.

## 7. Cómo se deshace

Solo lo que se agrega con esta guía, en este orden.

1. HCM: quitar las dos conexiones del servidor de pruebas.
2. Tarea de aviso: el bloque de reversa al final de `07_job_aviso_pedidos.sql`, con el nombre `Millet ERP - aviso de pedidos A+W (DEV)` y `USE MILLET_INTEGRACION_DEV`. La tabla de avisos no se toca.
3. Usuario de lectura: el bloque de reversa al final de `09_create_login_lector.sql`.
4. `hosts`: quitar `127.0.0.1 aw-sql-onprem` solo si se agregó con esta guía.

Los bloques de reversa están comentados al final de cada script: se copian, se descomentan y se ejecutan como sysadmin.
