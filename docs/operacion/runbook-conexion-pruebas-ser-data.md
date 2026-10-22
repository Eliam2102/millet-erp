# Conexión del servidor de pruebas con A+W en SER-DATA

Tarea: A+W-QA-1 (86e3uzwkx). Contrato que acompaña a esta guía: I13.4.

El servidor de pruebas del ERP vive en Azure (`rg-millet-qa-mxc-01`). SER-DATA (192.168.1.8, instancia `SER-DATA\AWBUSINESS`) está en la red interna de Millet, así que Azure llega a él por **Hybrid Connection**: el Hybrid Connection Manager (HCM) instalado en SER-DATA abre una conexión **saliente** por 443 hacia Azure Relay. No se abre ningún puerto de entrada en la red de Millet.

| Conexión | Destino que ve el HCM | Para qué |
|---|---|---|
| `hc-aw-business-sql` | `aw-sql-onprem:1433` | El ERP lee la cola de pedidos, las vistas `vw_erp_*` y los maestros de clientes y productos |
| `hc-aw-drop-service` | `aw-drop-onprem:5000` | El ERP deja los EDI de cotización y recoge PDF y resultados (servicio `MilletAwDropService`) |

`aw-sql-onprem` y `aw-drop-onprem` son nombres que sólo existen en el `hosts` de SER-DATA y apuntan a `127.0.0.1`. Se usan para que el HCM no intente IPv6 ni dependa del DNS de la red (incidente documentado en `infra/modules/hybridConnections.bicep`).

## 1. Quién hace qué

| Quién | Qué |
|---|---|
| Jorge (TI Millet) | Prerrequisitos en SER-DATA, scripts SQL, HCM y servicio de drop (secciones 2 a 5) |
| Eliam | Da a Jorge las dos cadenas del listener de Azure Relay y la `ApiKey` del servicio de drop; carga en Key Vault las cadenas de conexión del ERP |
| Equipo ERP | Entrega el paquete del servicio de drop (zip + SHA256) y enciende los flujos uno por uno (A+W-QA-7) |

Ningún paso de esta guía toca la base de producción `MILMAIN` con escritura. El ERP sólo lee de `MILMAIN` y sólo escribe en `MILLET_INTEGRACION_DEV` (cola y retorno).

## 2. Prerrequisitos en SER-DATA

- [ ] Windows Server con acceso de administrador y SQL Server Management Studio o `sqlcmd`.
- [ ] La instancia `AWBUSINESS` escucha en el **puerto fijo 1433** (hoy usa el dinámico 49900). Se cambia en *SQL Server Configuration Manager → Protocolos de AWBUSINESS → TCP/IP → IPAll*: `TCP Dynamic Ports` vacío y `TCP Port = 1433`. **Requiere reiniciar el servicio de SQL Server**: hacerlo en una ventana acordada.
- [ ] El **SQL Server Agent** de `AWBUSINESS` está en ejecución y con inicio automático (el aviso de pedidos es un job del Agent).
- [ ] Salida HTTPS 443 permitida hacia `*.servicebus.windows.net`.
- [ ] En `C:\Windows\System32\drivers\etc\hosts`:
  ```
  127.0.0.1 aw-sql-onprem
  127.0.0.1 aw-drop-onprem
  ```

## 3. Scripts SQL

Carpeta `docs/operacion/aw-integracion-scripts/`. Se corren en este orden, con un usuario con rol **sysadmin**. Todos se pueden repetir sin daño y cada uno trae al final el bloque para deshacerlo. Los scripts 02, 03 y 07 traen `USE MILLET_INTEGRACION;`: para pruebas se cambia a `USE MILLET_INTEGRACION_DEV;` antes de correrlos. En `07` también se cambia `DECLARE @base sysname = N'MILLET_INTEGRACION';` (sección 4 del script) a `N'MILLET_INTEGRACION_DEV'`, para que el job corra contra la base de pruebas.

| # | Script | Base | Qué hace |
|---|---|---|---|
| 1 | `01_create_database.sql` | `master` | Crea `MILLET_INTEGRACION` y `MILLET_INTEGRACION_DEV` con la misma intercalación que `MILMAIN`. Pruebas usa sólo `_DEV` |
| 2 | `02_create_table.sql` | `MILLET_INTEGRACION_DEV` | Crea la cola `aw_solicitud_pedido` |
| 3 | `03_create_views.sql` | `MILLET_INTEGRACION_DEV` | Crea las vistas `vw_erp_*` (sólo lectura sobre `MILMAIN`) |
| 4 | `04_create_login.sql` | `master` → `MILLET_INTEGRACION_DEV` | Login `millet_erp_integracion` para pedidos: lee vistas, escribe sólo el estado en la cola. Pide la contraseña con `:setvar Password` |
| 5 | `09_create_login_lector.sql` | `master` → `MILMAIN` | Login `millet_erp_lector_aw`: sólo `SELECT` sobre las 9 tablas de clientes y productos. Pide la contraseña con `:setvar Password` |
| 6 | `07_job_aviso_pedidos.sql` | `MILLET_INTEGRACION_DEV` + `msdb` | Job del Agent que cada 15 s escribe en la cola los pedidos que llegan a 15 o 42 y las cancelaciones (900/999). Si el botón del vendedor ya escribió el pedido, no lo repite |

No correr `05_seed_dev.sql` (datos de ejemplo), `07_create_otr_tables.sql` ni `08_grant_otr.sql` (OTR, sin acuerdo todavía).

Las contraseñas las genera Jorge, las guarda él y se las pasa a Eliam por un canal seguro para el Key Vault. No se escriben en ningún archivo ni correo.

## 4. Hybrid Connection Manager

1. Si el HCM no está instalado, descargarlo de Microsoft (*Hybrid Connection Manager*) e instalarlo en SER-DATA.
2. En el HCM: **Add a new Hybrid Connection → Enter Manually** y pegar la cadena del listener de `hc-aw-business-sql` que da Eliam. Repetir con `hc-aw-drop-service`.
3. Las dos deben quedar en **Connected** en menos de un minuto.

Si ya existen conexiones de desarrollo en el HCM, se dejan; las de pruebas se agregan aparte.

## 5. Servicio de drop (`MilletAwDropService`)

El equipo ERP entrega `MilletAwDropService-<versión>-win-x64.zip` con su SHA256. Jorge:

1. Comprueba el hash: `Get-FileHash .\MilletAwDropService-*.zip -Algorithm SHA256`.
2. Lo descomprime en `C:\Apps\MilletAwDropService` y copia `appsettings.example.json` como `appsettings.json`.
3. En `appsettings.json` pone la `ApiKey` que le da Eliam y las carpetas de A+W (`AwImportFolder`, `ResultsFolder`, `Documents.Folders`). `ListenUrl` se queda en `http://localhost:5000`.
4. En PowerShell como administrador: `cd C:\Apps\MilletAwDropService\install; .\install-service.ps1`.
5. Verifica con `.\verify-install.ps1`.

## 6. Cifrado

La conexión con SQL va cifrada de punta a punta (`Encrypt=True`). El ERP acepta el certificado que SQL Server genera solo, únicamente hacia `aw-sql-onprem` y con una clave de configuración explícita. Jorge no tiene que hacer nada para esto. Antes de producción se instala un certificado propio en SER-DATA con el nombre `aw-sql-onprem`, para que el ERP también lo verifique.

> Verificación pendiente: esta opción se prueba primero en un ambiente de ensayo con SQL Server 2016 antes de aplicarla en SER-DATA.

## 7. Cómo se comprueba que conecta

| Prueba | Dónde | Resultado esperado |
|---|---|---|
| HCM | SER-DATA | Las dos conexiones en *Connected* |
| Job del aviso | SSMS → SQL Server Agent → Jobs | `Millet ERP - aviso de pedidos A+W` con ejecuciones correctas cada 15 s |
| Servicio de drop | `Invoke-WebRequest http://localhost:5000/healthz` en SER-DATA | `200` con `import_folder_writable=true` |
| Conexión del ERP | SSMS: `SELECT client_net_address, encrypt_option, auth_scheme FROM sys.dm_exec_connections WHERE session_id IN (SELECT session_id FROM sys.dm_exec_sessions WHERE login_name LIKE 'millet_erp_%')` | `127.0.0.1` y `TRUE` |
| Pedidos | ERP de pruebas → Facturación | Un pedido que pasa a 15 o 42 aparece en menos de 1 minuto |

## 8. Cómo se deshace

En orden inverso, sin tocar `MILMAIN`:

1. Servicio de drop: `C:\Apps\MilletAwDropService\install\uninstall-service.ps1`.
2. HCM: quitar las dos conexiones de pruebas.
3. Job: `EXEC msdb.dbo.sp_delete_job @job_name = N'Millet ERP - aviso de pedidos A+W';` (o el bloque final de `07_job_aviso_pedidos.sql`).
4. Bloques de reversa al final de `09`, `04`, `03`, `02` y `01`, en ese orden. El de `01` borra las dos bases de integración completas (si `MILLET_INTEGRACION` no se usa en producción todavía, basta con borrar `_DEV`).
5. Quitar las dos líneas del `hosts`.
6. El puerto 1433 se puede dejar fijo; si se quiere regresar al dinámico, es el paso inverso de la sección 2 y otro reinicio.
