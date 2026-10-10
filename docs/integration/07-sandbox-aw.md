# 07 — Sandbox local de A+W (O1A-AW-INT, fase sin Hybrid Connection)

> **Estado:** herramienta de prueba. **No sustituye** la integración real: O1A-AW-INT sigue abierta hasta probar la ruta Azure → A+W (Hybrid Connection, TLS verificado).
> Complementa [05](05-sincronizacion-clientes-aw.md) y [06](06-sincronizacion-productos-aw.md), cuyo "fixture ≠ integración real" sigue vigente.

## Qué es
SQL Server 2022 en compatibilidad 130 (= 2016, como `SER-DATA\AWBUSINESS`) con el esquema `SYSADM` **íntegro del A+W real** (solo estructura: las 755 tablas con tipos, nulabilidad, IDENTITY, collation `Latin1_General_CS_AS`, 714 PK y 693 índices secundarios = los 1 407 índices del real) y datos 100 % sintéticos con las rarezas reales (`BA_PRODUKT=0`, `<indf>`, `PESOSMX`, medidas 0 = sin dato, `KZ_GESPERRT` 0/1/2, `BA_MCODE` y `UST_ID` repetidos, composiciones `6+0.89+6` y `3+12+3`). Los lectores reales (`AwClientesSqlOrigen`, `AwProductosSqlOrigen`) y los sincronizadores corren contra él y contra PostgreSQL.

## Uso
```bash
cd tools/aw-sandbox && cp .env.example .env   # poner contraseñas locales (.env está ignorado)
./aw-sandbox.sh up            # levanta (127.0.0.1:14330), aplica esquema y seed
./aw-sandbox.sh mutar <caso>  # actualizar-cliente, duplicar-cliente, invalidar-moneda, invalidar-estado,
                              # bloquear-producto, actualizar-producto, cambiar-dias-catalogo, borrar-fila, unidad-desconocida
./aw-sandbox.sh reseed|reset|down
./extraer-esquema-completo.sh > schema/full/01-esquema-completo.sql   # (VPN) DDL íntegro del real, solo SELECT
./extraer-indices.sh > schema/full/02-indices.sql                      # (VPN) índices secundarios del real, solo SELECT
./perfilar.sh real|sandbox    # agregados sin PII para diff
```
Pruebas (opt-in; sin `AW_SANDBOX_CONN` retornan temprano en verde): `dotnet test backend/tests/Api.IntegrationTests --filter "Category=AwSandbox"`.
Requieren una BD PostgreSQL desechable (`TestAssemblyInit`), nunca `millet_dev`.

## Cobertura (10 clientes + 10 productos, todas verdes)
consulta/recepción paginada · actualización (fiscales/clave SAT del operador no se pisan) · duplicado (RFC/`UST_ID`/`BA_MCODE` repetidos no fusionan; barrido repetido = `SinCambios`) · dato inválido (moneda/estado/unidad) · baja y "ausencia ≠ baja" · conflicto de versión · fallo a mitad (contenedor detenido) · credenciales erróneas (error `auth`, sin filtrar la contraseña) · reintento (converge) · conciliación origen/destino con diferencias explícitas (`AwConciliacion`).

El seed sintético solo informa las columnas relevantes: `03-defaults-temporales.sql` pone defaults mientras se siembra y `04-quitar-defaults.sql` los quita, de modo que la estructura final es idéntica a la real. **Nunca** usar `reset` si `AW_FULL` está cargada: borra el volumen `aw_sandbox_data` (usar `reseed`).

## Qué NO cubre
Hybrid Connection, TLS verificado (el test usa `TrustServerCertificate` solo aquí), latencia y volumen reales (150 clientes / 160 productos vs 44 695 / 6 802), ventanas de barrido, cambios que haga A+W en producción.

## Hallazgos del comportamiento real de los lectores/sincronizadores
1. **Clientes: casi todo queda `Pendiente`.** `AplicarClienteAwService` marca `Pendiente` siempre que `KZ_STATUS` no es nulo; en A+W real solo 1 de 44 695 es nulo. Es coherente con "mapeo de estado PENDIENTE", pero un barrido real no aplicará estado hasta que Millet lo apruebe. `Pendiente` no distingue moneda/estado/condición.
2. Cliente **nuevo** con moneda sin equivalencia no se crea (error `moneda_sin_equivalencia`); solo un cliente existente queda `Pendiente`.
3. Una ejecución de clientes `Fallida` es terminal: el reintento es un barrido nuevo (lo aplicado da `SinCambios`), no una reanudación.
4. Total múltiplo del lote ⇒ una página final vacía extra (inocuo).
5. **Productos:** sin ejecución durable; un fallo de origen sale como `AwReaderException` y lo aplicado antes queda aplicado. Los productos `UNIDAD_SIN_EQUIVALENCIA` (`<indf>`, sin descripción en idioma 0) no dejan registro y se reportan en cada barrido.
6. `KA_ZAHLBED.BEZ` es PK en el real: "ZAHLBED con >1 coincidencia" no puede ocurrir (el caso 6 de [05](05-sincronizacion-clientes-aw.md) §9 solo aplica a 0 coincidencias).

## Comparación con el A+W real
Perfil sin PII de real vs sandbox en `.local-context/evidencias/o1a-aw-int/` (`perfil-real.txt`, `perfil-sandbox.txt`): mismas métricas y valores de catálogo; diferencias conocidas: `BOM_LEVEL` 3/4 y cuatro plazos (`7/15/21/75 DIAS`) sin sembrar, y dos `ZAHLBED` sintéticos (`CREDITO DEMO`, `contado`) para cubrir "sin coincidencia" (el real tiene 0).

## BD completa `AW_FULL` (copia de pruebas con datos reales)
Segunda BD en el mismo contenedor (no toca `AW_SANDBOX`, que sigue siendo el fixture sintético de los tests): las **755 tablas** de `SYSADM` con el DDL real (columnas, `IDENTITY`, PK; sin índices secundarios) y datos reales de los **últimos 2 años**.
- Cotizaciones/pedidos/notas de crédito (`BW_ANGEB_*`, `BW_AUFTR_*`, `BW_GUTSCH_*`): por `DATUM_ERF` de su cabecera `*_KOPF`; las tablas hijas, por `ID` de esas cabeceras. Logs grandes (`BW_LOGBOOK`, `FS_PROTOCOLL*`, `FS_POOL*`, `PD_AWBAR`, `LG_LOGBUCH`): por su fecha. Resto (maestros, catálogos): completo.
- Uso (requiere VPN): `tools/aw-sandbox/extraer-esquema-completo.sh > schema/full/01-esquema-completo.sql` (solo estructura), `tools/aw-sandbox/cargar-completo.sh [esquema|datos|todo]` (solo `SELECT` al real vía `bcp queryout`; reanudable; `AW_ANIOS`, `AW_DESDE`, `PAR`).
- Lector: `aw_ro` (solo `SELECT` sobre `SYSADM`), `localhost,14330`, BD `AW_FULL`.
- **Datos reales (PII de clientes): solo viven en el volumen Docker `aw_sandbox_data`, fuera del repo.** Prohibido volcarlos a seeds, evidencias o commits. Decisión del owner (2026-10-02), levanta la restricción "solo estructura y agregados" del plan para esta BD local.


## Copia PostgreSQL alternable de demo (AW-DEMO, 9-oct-2026)

Conviven el camino vigente de A+W (`Real`, SQL Server o simulado según cada área) y una copia independiente
(`Demo`, PostgreSQL). Los adaptadores de copia están en `Infrastructure/OrigenPg/`; los lectores SQL Server,
sus parámetros y su manejo de errores se conservan. La demo no sustituye Azure Hybrid Connection ni
acredita una integración en vivo. Se puede cargar una copia de datos de Millet en la base de demo; esos
datos nunca se guardan en archivos versionados del repositorio. `seed.sql` contiene únicamente datos sintéticos.

### Preparar la copia

Desde un equipo con Docker y el PostgreSQL local ya disponible:

```sh
bash tools/aw-origen-demo/aw-origen-demo.sh up
# Muestra SOLO la conexión PostgreSQL de copia y el permiso de ambiente:
bash tools/aw-origen-demo/aw-origen-demo.sh env
```

`up` crea `millet_aw_origen` y aplica `schema.sql`, `seed.sql` y `pedidos.sql` (60 clientes, 47 productos,
3 solicitudes). `env` exporta únicamente `ConnectionStrings__AwOrigenPgDb` y
`IntegracionesAw__OrigenDemo__Permitido=true`; **no elige el origen** ni reconfigura los modos reales.
Conservar la conexión fuera del repositorio. `reseed` restaura clientes/productos sintéticos; `pedidos`
restaura la cola de copia. Re-sembrar pedidos exige un ERP de prueba nuevo o limpiar sus controles de
ingesta sintéticos, porque la idempotencia del ERP recuerda las versiones procesadas.

Para copiar desde el sandbox A+W o una copia autorizada, `aw-origen-demo.sh importar` invoca
`importar-desde-aw.cs`: copia clientes, condiciones y productos en una transacción PostgreSQL, sin exportar
clientes/proveedores a archivos del repositorio. Usa credenciales de lectura de SQL Server; la herramienta
no escribe en A+W. El importador reemplaza el contenido de `aw_origen.*` y el script vuelve a preparar los
pedidos de demo. Las credenciales y datos originales se mantienen fuera de Git.

### Habilitar y usar el control

- `IntegracionesAw:OrigenDemo:Permitido` es `false` por defecto y `true` en Development.
- En QA o demo de Azure habilitar explícitamente esta opción y configurar
  `ConnectionStrings:AwOrigenPgDb` (App Settings/Key Vault). Usar un ambiente `QA` o `Demo`;
  `Production` bloquea siempre el cambio, incluso con la opción encendida.
- Se conservan los candados operativos de sincronización: clientes exige `LecturaHabilitada` y
  `AplicacionHabilitada`; productos exige `OrigenHabilitado`. Para pedidos se conservan los candados del
  worker (`Disabled`, `EmpresaId`), las equivalencias de sucursal/canal y el resto del doc 04.
- Un usuario con `integraciones.aw.administracion.configuracion` ve «A+W real / Copia de demo» en ambos
  sheets de sincronización de Datos maestros. El cambio solicita confirmación porque la sincronización
  **guarda datos en esta base del ERP**. Los usuarios de esos sheets siempre ven la etiqueta del origen.

El origen se persiste en `compartido.parametros_globales`, clave `integraciones.aw.origen-activo`, Id
`00000006-0001-0000-0000-00000000000c`, valor inicial `Real`. En la serie existente se verificaron
`…0001` a `…000a`; `…000b` se respeta como reservado. La migración solo agrega este parámetro.
El PATCH genérico de parámetros rechaza esta clave; no permite eludir los controles del endpoint de A+W.

`GET /api/v1/integraciones/aw/origen` devuelve origen activo, permiso de ambiente, configuración de copia,
modo real por área, último actor/fecha y versión (ETag). Está disponible a usuarios autenticados para mostrar
la etiqueta. `PUT` exige el permiso administrativo y `Idempotency-Key`; acepta `If-Match` y devuelve ETag.
Un ETag obsoleto produce 409. Cada cambio real genera `AuditLogEntry` mediante el interceptor vigente, con
actor y diff de valor anterior/nuevo en la misma transacción. Repetir el valor vigente no crea un cambio ficticio.

Los selectores son scoped y renuevan la selección al iniciar cada barrido, lectura por referencia y ciclo
de solicitudes. Las páginas, masters y write-back de ese ciclo conservan el origen elegido, por lo que un
cambio administrativo durante un ciclo aplica al siguiente. No se cachea en un singleton ni requiere reiniciar.
El despachador de clientes también relee el origen activo por ciclo para ejecutar los barridos encolados
como `Demo`; al volver a `Real` atiende los del modo configurado en el área (`Sql` o `Simulado`).
La fábrica PostgreSQL consulta `AwOrigenPgDb` al crear cada conexión, aun si el adaptador ya estaba resuelto.
Elegir `Demo` sin conexión resuelta produce 422: «La copia de demo de A+W no está configurada en este ambiente».
Con el ambiente bloqueado produce 422: «El origen de demo no está permitido en este ambiente».
No existe fallback silencioso.
En `Real` con clientes `Sql` sin cadena, usar el selector falla con
«Origen 'Sql' sin adaptador: falta ConnectionStrings:AwClientesDb.»; el barrido o reintento queda
`Fallida` con esa explicación, sin leer de otro origen.

Las reglas fiscales de productos por `BA_PRODUKTART` se reutilizan de la rama de Geovany
(VID80000–VID80003) y se aplican en ambos orígenes; solo completan campos vacíos. RFC/CP de clientes
solo se aplican desde la copia de demo; en Real siguen pendientes de Fiscal, según el doc 05 §4.

### Verificación pendiente en PostgreSQL desechable

`AwOrigenEndpointsTests`, `AwOrigenPgTests` y `AwOrigenPgPedidosTests` cubren permiso, ambiente,
auditoría, concurrencia, sincronización, reglas fiscales, ingesta y write-back. Limpian sus catálogos y
pedidos sintéticos. Ejecutar `tools/validate-integration-isolated.sh` completo en un equipo con Docker;
las pruebas escritas/compiladas no equivalen a una corrida verde ni a aceptación de Millet.
