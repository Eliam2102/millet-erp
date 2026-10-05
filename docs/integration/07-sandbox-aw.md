# 07 — Sandbox local de A+W (O1A-AW-INT, fase sin Hybrid Connection)

> **Estado:** herramienta de prueba. **No sustituye** la integración real: O1A-AW-INT sigue abierta hasta probar la ruta Azure → A+W (Hybrid Connection, TLS verificado).
> Complementa [05](05-sincronizacion-clientes-aw.md) y [06](06-sincronizacion-productos-aw.md), cuyo "fixture ≠ integración real" sigue vigente.

## Qué es
SQL Server 2022 en compatibilidad 130 (= 2016, como `SER-DATA\AWBUSINESS`) con el esquema `SYSADM` **extraído del A+W real** (solo estructura: tipos, nulabilidad, collation `Latin1_General_CS_AS`, PK/índices de las 5 tablas que leen los lectores) y datos 100 % sintéticos con las rarezas reales (`BA_PRODUKT=0`, `<indf>`, `PESOSMX`, medidas 0 = sin dato, `KZ_GESPERRT` 0/1/2, `BA_MCODE` y `UST_ID` repetidos, composiciones `6+0.89+6` y `3+12+3`). Los lectores reales (`AwClientesSqlOrigen`, `AwProductosSqlOrigen`) y los sincronizadores corren contra él y contra PostgreSQL.

## Uso
```bash
cd tools/aw-sandbox && cp .env.example .env   # poner contraseñas locales (.env está ignorado)
./aw-sandbox.sh up            # levanta (127.0.0.1:14330), aplica esquema y seed
./aw-sandbox.sh mutar <caso>  # actualizar-cliente, duplicar-cliente, invalidar-moneda, invalidar-estado,
                              # bloquear-producto, actualizar-producto, cambiar-dias-catalogo, borrar-fila, unidad-desconocida
./aw-sandbox.sh reseed|reset|down
./extraer-esquema.sh          # (VPN) regenera schema/01-esquema.sql desde el A+W real, solo SELECT
./perfilar.sh real|sandbox    # agregados sin PII para diff
```
Pruebas (opt-in; sin `AW_SANDBOX_CONN` retornan temprano en verde): `dotnet test backend/tests/Api.IntegrationTests --filter "Category=AwSandbox"`.
Requieren una BD PostgreSQL desechable (`TestAssemblyInit`), nunca `millet_dev`.

## Cobertura (10 clientes + 10 productos, todas verdes)
consulta/recepción paginada · actualización (fiscales/clave SAT del operador no se pisan) · duplicado (RFC/`UST_ID`/`BA_MCODE` repetidos no fusionan; barrido repetido = `SinCambios`) · dato inválido (moneda/estado/unidad) · baja y "ausencia ≠ baja" · conflicto de versión · fallo a mitad (contenedor detenido) · credenciales erróneas (error `auth`, sin filtrar la contraseña) · reintento (converge) · conciliación origen/destino con diferencias explícitas (`AwConciliacion`).

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
