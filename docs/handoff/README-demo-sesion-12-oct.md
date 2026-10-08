# Datos DEMO · sesión del 12-oct-2026

`DemoSesionSeedHostedService` prepara una base **local, recién migrada**, de una empresa. Solo corre con **Development + `Seed:DemoSesion:Habilitado=true`**. En Production, Staging o con bandera ausente/apagada no abre ningún servicio ni conexión.

## Activación

Apuntar `ConnectionStrings:Postgres` a `millet_demo` y aplicar los 13 contextos de `tools/migration-contexts.txt`. Inyectar la configuración por variables de proceso o secretos locales; no guardar credenciales en Git:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export Seed__DemoSesion__Habilitado=true
# Opcional: omitir la semilla anterior de empleados y puestos ficticios.
export Seed__DatosDemo__Habilitado=false
export Seed__DemoSesion__Usuarios__0__Correo='compras-demo@tu-dominio'
export Seed__DemoSesion__Usuarios__0__Rol='Compras'
export Seed__DemoSesion__Usuarios__0__Sucursales__0='MID'
dotnet run --project backend/src/Api/Millet.Api.csproj --no-launch-profile --urls http://localhost:5091
```

Usar los adaptadores de blobs locales (sin `Adjuntos:BlobStorage:ConnectionString` ni `Compras:Oc:BlobStorage:ConnectionString`). Los PDF se generan en memoria y se guardan mediante los puertos existentes, con nombre y contenido ficticios. No se crean PAC, API keys, CSD ni usuarios de Entra.

El sembrador corre después de catálogos y bootstrap. `CatalogosTestSeedHostedService` conserva MID/MTY/QRO, departamentos y asignaciones, pero omite sus proveedores y artículos de prueba solo en esta combinación. Fuera de ella conserva su comportamiento anterior. No elimina datos de una base usada: para una demo limpia se parte de una base nueva, no de la base de las suites.

## Usuarios

La lista `Seed:DemoSesion:Usuarios` admite objetos `{ correo, rol, sucursales: [] }`. Roles exactos: `Compras`, `CxP`, `Tesorería`, `Facturación`, `Contabilidad`, `Administrador`. Sucursales: MID/MTY/QRO; **Compras exige solo MID**.

Solo asigna usuarios ya existentes por correo, sin distinguir mayúsculas. Si falta uno, registra **Por confirmar**, continúa preparando los datos y pide iniciar sesión con Entra y reiniciar el API. Los roles se llaman `DEMO …`. Para los correos configurados, la lista reemplaza roles y excepciones individuales de permiso dentro de la empresa DEMO, y desactiva sucursales no listadas; evita que un super-admin heredado invalide el rechazo de MTY. Usar cuentas dedicadas para el ensayo y volver a iniciar sesión para renovar sus permisos.

Compras incluye operación de Almacén para recibir en la demo, sin permisos de acceso a todas las sucursales. Los demás perfiles operativos reciben permisos de su módulo; Administrador recibe los permisos canónicos. Estas son asignaciones ficticias para el ensayo, no la matriz definitiva de Millet. Las autorizaciones y adjuntos iniciales usan un identificador técnico `DEMO-SEMBRADOR`, sin crear una cuenta ni atribuir las firmas a una persona.

## Datos y escenas

| Escena indicada en la ficha | Datos iniciales / uso |
|---|---|
| Organización; número por confirmar | Empresa bootstrap, razón social `MILLET INDUSTRIA DE VIDRIO (DEMO)`, régimen 601, CP 42501. Conserva RFC/clave del bootstrap existente; no crea otra empresa. Conserva las tres sucursales canónicas, sin inventar las seis unidades pendientes V49. |
| **2 · Rechazo por sucursal** | `DEMO-OC-MTY`, folio `OC-DEMO2026-000102`; usuario Compras con MID únicamente. |
| **5 · Expediente de proveedor** | `DEMO-PROV-REV` en revisión, tres de los cinco tipos obligatorios. `DEMO-PROV-ACT` activo, cinco PDF, banco y CLABE explícitamente ficticios. |
| **6 · Compra y recepción parcial** | `DEMO-RQ-MID`, folio `DEMO2026-000001`, N1/N2 aprobadas, diez piezas por comprar y vínculo a `DEMO-OC-MID`. La RQ está **EnSurtido** tras registrar cubrimiento, conforme al dominio. OC `OC-DEMO2026-000101` autorizada N1/N2, diez piezas de `DEMO-ART-PIEZA`, cero recibidas y adjuntos `cotizacion`/`correo_autorizacion`. Recibir seis en pantalla. |
| Facturación normal; número por confirmar | `DEMO-PED-NORMAL` → `DEMO-CLI-NORMAL`. Diez piezas a $100, IVA 16 %. Serie continua `DEMO-F`. |
| Facturación con ranura; número por confirmar | `DEMO-PED-RANURA` → `DEMO-CLI-RANURA`, `Ranura=116` brutos. Origen A+W ficticio mediante `ImportarDesdeAw`; `NcRanuraEmisor` actúa al timbrar. Serie continua `DEMO-NC`. |
| U1.9; número por confirmar | `DEMO-PED-INCOMPLETO` → `DEMO-CLI-INCOMPLETO`, sin RFC/régimen/CP, para mostrar el bloqueo fiscal. |
| Tesorería; número por confirmar | Bancos `DEMO-CTA-MXN` y `DEMO-CTA-USD`, números alfanuméricos `DEMOCTAMXN`/`DEMOCTAUSD` (el dominio no admite guiones en números de cuenta). No hay pagos ni movimientos preaplicados. |
| **10 · Contabilidad** | Ejercicio 2026 con 13 periodos. Enero–septiembre cerrados secuencialmente, octubre abierto, noviembre/diciembre/ajuste sin abrir. Catálogo ficticio de cinco cuentas 990.*, lote `DEMO-SESION`: título, banco, colectiva de clientes, gasto y naturaleza por confirmar. |

La ficha no enumera el contenido de las escenas **1, 3, 4, 7, 8, 9 y 11**: su numeración exacta queda **Por confirmar**. Los datos solicitados están identificados arriba sin inventar ese guion. Los pasos de recepción, facturación, cobro y pago se realizan durante el ensayo; esta semilla no acredita su integración.

Clientes fiscales: `DEMO-CLI-NORMAL` (IIA040805DZ4/62661), `DEMO-CLI-RANURA` (IVD920810GU2/63901), `DEMO-CLI-KIJ` (KIJ0906199R1/28971) y `DEMO-CLI-XIA` (XIA190128J61/76343), régimen 601. Conservan las razones sociales **literales y ficticias** de [FiscalAPI](https://docs.fiscalapi.com/testing-data), consultadas el 07-oct; añadir DEMO al nombre fiscal rompería la coincidencia del receptor. El emisor, PAC y CSD deben validarse en pantalla antes de timbrar: la empresa de presentación no acredita que sus datos coincidan con el certificado sandbox.

## Repetición y límites

Las claves DEMO y sus IDs deterministas, más un candado PostgreSQL reutilizado, impiden duplicación entre arranques. Cada módulo confirma sus datos mediante EF y métodos de dominio; las autorizaciones de compra encolan Outbox **antes** de `SaveChanges`. No hay SQL de negocio nuevo. La RQ registra cubrimiento explícito para evitar que el flujo automático cree otra OC.

Un reinicio conserva las operaciones hechas en pantalla: no devuelve una OC recibida a cero, no desvalida un proveedor, no regenera pedidos facturados ni recierra un periodo reabierto. Para repetir desde cero, preparar otra base DEMO recién migrada. Ante un fallo parcial, reintentar completa las etapas faltantes. Los PDF se compensan si falla su guardado; los pasos completados permanecen.

El catálogo usa el **motor de importación C1.0** y sus reglas de formato/jerarquía, con cinco filas ficticias y correspondencias `DEMO-CTA-*`. No contiene el Excel privado de Laura ni sustituye sus 739 cuentas. Rechaza colisiones con cuentas 990.* ajenas al lote o una configuración contable incompatible. Ver [C1.0](../modulos/contabilidad/09-c1-0-cuentas-control-y-carga-del-catalogo.md).

## Verificación y entrega a Claude

Prueba `Administracion.DemoSesionSeedTests`: crea otra base temporal en el PostgreSQL del gate, aplica las 13 migraciones, arranca dos veces, verifica conteos/estados, enlaces a clientes, PDF legibles, Outbox y asignación de un usuario preexistente. También comprueba que un reintento conserve seis piezas recibidas. Elimina base y blobs en `finally`; no cambia los conteos de otras suites. Los inicializadores de las tres suites fuerzan la bandera apagada; solo este host la activa.

Unitarias nuevas en `Api.UnitTests`: guardas Development/bandera, configuración de roles/sucursales, ausencia de bypass para Compras, importación del catálogo y PDF real en memoria.

Resultados verificados en este worktree (07-oct-2026):

| Comprobación | Resultado |
|---|---|
| `dotnet build Millet.sln --no-restore --disable-build-servers -m:1` | 0 errores, 0 advertencias. Restauración previa desde la caché local de NuGet. |
| xUnit dentro del proceso | **1,721 pasadas, 0 fallidas, 0 omitidas**: Api 12, Compartido 68, Compras 533, Facturación 342, Contabilidad 167, Tesorería 105, Identidad 103, Administración 140, SharedKernel 251. |
| `npx tsc --noEmit -p tsconfig.json` | Código de salida 0. |
| `npm run -s typecheck:test` | Código de salida 0. |
| `npm run -s lint` | 0 errores; 10 advertencias en frontend no modificado. |
| `npx vitest run` | 331 archivos y 1,942 pruebas pasadas. |
| `git diff --check` | Sin errores. |

`dotnet test` fue intentado y **se abortó antes de ejecutar casos** por `SocketException (13): Permission denied` al abrir el socket de VSTest. El resultado de unitarias anterior procede del ejecutor oficial xUnit `AssemblyRunner.WithoutAppDomain`, cargando los mismos assemblies y fijando su directorio base al de cada suite, sin sockets. Runner temporal: `/tmp/millet-demo-xunit/`; salidas `/tmp/millet-demo-inprocess-*.log`. No se modificaron pruebas existentes para obtener el verde. En un entorno sin esa restricción, ejecutar las suites con `dotnet test` normalmente.

**Pendiente obligatorio:** Claude debe ejecutar `tools/validate-integration-isolated.sh` completo y documentar rojo/verde. No se ejecutó integración PostgreSQL desde este sandbox, que no tiene Docker. Tampoco se arrancó `millet_demo`, se configuró PAC/CSD ni se ensayaron las 11 escenas.

La nota para la bóveda es esta entrega local: construcción y validaciones de código, sin acreditación de demo operativa/UAT. Falta incorporarla a la nota de sesión y Bitácora; la bóveda queda fuera del worktree autorizado.
