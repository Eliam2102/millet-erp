# 06 — Sincronización de productos, claves y unidades A+W → ERP (F1-ADM-07)

> **Versión del documento:** 0.1.0 (borrador de contrato)
> **VersionContrato:** `1` · **VersionMapeo:** `0-borrador`
> **Documento hermano (patrón a copiar):** [05 — Sincronización de clientes](05-sincronizacion-clientes-aw.md)
> **Contexto:** [04 — Ingesta de pedidos](04-ingesta-pedidos-facturacion.md) §3.5 (`vw_erp_articulo`)
> **Estado:** especificación previa a construcción. Todo lo no validado contra A+W real está marcado **PENDIENTE**.

---

## 0. Limitación explícita: fixture ≠ integración real

**Los fixtures de este contrato son sintéticos y NO demuestran integración real con A+W.** Prueban la forma esperada y la política de sincronización del lado ERP, nada más. La lectura real de productos contra A+W depende de:

- **O1A-AW-MAP** — mapeo de columnas/vistas de A+W (responsable: Eliam). El esquema real se **verificó en solo lectura contra `MILMAIN` el 2026-10-01** (`SYSADM.BA_PRODUKTE`, `BA_PRODUKTE_BEZ`, `BA_STUKL`) y el lector SQL se escribió con esas columnas (§3). Quedan **pendientes de confirmar con Millet** los criterios de negocio (tipos de producto, significado de `KZ_GESPERRT`, composición oficial); ver §11 y `Documentos/ADM07_Preguntas_para_Eliam.md`.
- **O1A-AW-INT** — lectura real contra A+W a través de la Hybrid Connection.

El cierre de ADM-07 es **construcción técnica con fixtures**. No debe afirmarse en doc, PR ni bitácora que la sincronización de productos funcione contra A+W real.

Los ejemplos y fixtures usan datos 100 % sintéticos (`DEMO-P001`, `PRODUCTO DEMO 001`).

## 1. Alcance y no-alcance

**Alcance:**
- Sincronizar a `ProductoAw` (`compartido.producto_aw`, DatosMaestros) descripción, unidad, medidas y composición provenientes de A+W, con variantes por producto.
- Baja controlada, idempotencia, conflicto de versión y contrato de lectura estable para consumidores (Facturación primero).

**No-alcance:**
- Existencias, costos ni inventario (el ERP no maneja inventario de producto vendible).
- Escribir en A+W (módulo solo lectura).
- Entidades paralelas a `Articulo` (lo que la empresa compra): `ProductoAw` sigue separado (ADR-0048 D5).
- Herencia o "producto base" para productos tratados (YAGNI, ver §5).

## 2. Identidad

| Concepto | Regla |
|---|---|
| Llave estable | `ProductoAw.ReferenciaExterna` = `PROD_ID` de A+W (`producto_ref`), máx. 50, UNIQUE. |
| Duplicado | Mismo `producto_ref` repetido -> upsert idempotente por llave + hash de origen; nunca segundo producto. |
| Variante | `ProductoAwVariante.clave_variante`, llave natural dentro del producto: UNIQUE (`producto_id`, `clave_variante`). |

## 3. Contrato de datos (matriz campo -> dueño -> política)

| Campo | Origen A+W | Dueño | Política |
|---|---|---|---|
| `ReferenciaExterna` | `BA_PRODUKTE.BA_PRODUKT` (entero, PK con 221 FKs). **No** `BA_MCODE`: es un código de familia y se repite (728 códigos en 3,223 productos). | A+W | Inmutable; llave. |
| `Descripcion` | `BA_PRODUKTE_BEZ.BA_BEZ1`+`BA_BEZ2`+`BA_BEZ3` (idioma 0, unidas con espacio); respaldo `BA_MCODE` | A+W | Se actualiza desde A+W (máx. 254). |
| `UnidadMedida` / `UnidadMedidaId` | `BA_PRODUKTE_BEZ.BA_MENGENEINH` (`m²`, `Pza`, `m lin.`, `Kg`, `m³`, `ltr`, `m`; `<indf>` = sin dato) | A+W + catálogo ERP | Se normaliza (`m²`→`M2`, `m lin.`→`M`, `ltr`→`L`, `m³`→`M3`, `<indf>`→null; **no** `ML`, que en el seed es mililitro) y se resuelve contra `UnidadMedida` (ADR-0046) y `MapeoUnidadSat`; `M2` (dimensión Área) y `M3` (Volumen, 1 m³ = 1000 L) existen en el catálogo; ver §6. |
| Variantes: `alto_mm`, `ancho_mm`, `espesor_mm`, `composicion` | Una variante `BASE` por producto: espesor `BA_MASS_DICKE`, alto/ancho `BA_STD_HOEHE`/`BA_STD_BREITE` (0 = sin dato → nulo), composición: capas de `BA_STUKL` nivel 1 **con espesor** | A+W | Se actualizan desde A+W. Nulo != 0. A+W casi no modela variantes (`BA_STUKL.VARIANTE` en 11 filas): cada espesor es un producto distinto. |
| `ClaveProdServSat`, `ClaveUnidadSat`, `ObjetoImp`, tasas | No existen en A+W | Operador / Facturación | **Nunca se sobrescriben** desde A+W si el operador ya los completó. |
| Datos de aduana (`FraccionArancelaria`, `UnidadAduana`, `PesoUnitarioKg`) | No existen en A+W (`KA_ZOLLNR_PROD` vacía). `BA_MASS_GEWICHT` es **kg/m²**, no peso unitario: no se sincroniza | Operador | Igual que fiscales: lo capturado no se pisa. |
| `Estatus` / `FechaBaja` | `KZ_GESPERRT <> 0` = baja (criterio **por confirmar con Millet**; valores 0, 1 y 2). A+W no trae fecha de baja | Sincronización | Ver §7. |
| `Version` | — | ERP | Concurrencia optimista, ver §8. |

## 4. Nulo != 0 != desconocido

Las medidas (`alto_mm`, `ancho_mm`, `espesor_mm`) son nullables: **nulo significa "no informado"**, nunca 0. Una sincronización no convierte nulos en cero ni al revés.

## 5. Variantes y productos tratados

- **Dos medidas del mismo código** = 1 `ProductoAw` con 2 `ProductoAwVariante`.
- **Productos tratados** (templado, laminado) son productos con **códigos A+W distintos**; no hay herencia ni producto base. La composición es un **texto resumido de las capas de nivel 1 de `BA_STUKL` con espesor** (`6+0.89+6`, `3+12+3`); solo cuentan las capas con espesor (`BA_MASS_DICKE` > 0); las capas de proceso (`Proceso`, p. ej. canteado) y los kits (`Producto`) sin espesor se ignoran y esos productos quedan con composición nula. Solo 2,144 de 6,799 productos tienen lista de materiales; el resto queda con composición nula. `BA_PRODUKT_AUFBAU` **no** se usa: son cadenas por línea de documento, sin vínculo con el producto.

## 6. Unidad desconocida

`M2` y `M3` ya tienen equivalencia en el catálogo; el resto de unidades sin equivalencia sigue este camino. Si la unidad no tiene equivalencia en `UnidadMedida` + `MapeoUnidadSat`: **no se persiste dato inválido**. La fila de sincronización queda `RequiereRevision` con causa `UNIDAD_SIN_EQUIVALENCIA` y el `ProductoAw` no se crea ni actualiza. Se resuelve completando el mapeo y reintentando.

## 7. Baja controlada

- A+W deja de traer el producto o lo marca en baja -> `Estatus = Inactivo` + `FechaBaja`. **Nunca DELETE.**
- La ausencia de una fila en un lote **no** es baja (misma regla que clientes); solo la baja explícita lo es. Detalle PENDIENTE de confirmar con Millet (§11).
- Inactivo bloquea **uso nuevo** (lookups de Facturación para alta de líneas filtran por Activo); la consulta histórica por id sigue funcionando y las líneas ya facturadas no cambian.

## 8. Conflicto de versión

`Version` (long) implementa concurrencia optimista (ETag/If-Match, ADR-0012). Si una edición manual y una sincronización compiten, o dos escritores usan una versión vieja, el resultado es `ConflictoVersion`: **no se sobrescribe en silencio**. Mismo mecanismo que `ClienteSincronizacionAw`.

## 9. Contrato de lectura y endpoints (D1/D2)

**Puerto:** `IProductoAwContratoReadPort` (`ObtenerPorReferenciaAsync`, `ObtenerPorIdAsync`) en `Millet.DatosMaestros.Application.ProductosAw`; adaptador `ProductoAwContratoReadAdapter` sobre `CompartidoDbContext` (solo lectura). Devuelve inactivos con su `estatus`; nunca borra. Inventario, producción y facturación futuros lo consumen sin tocar tablas.

**`ProductoAwContratoV1`** (`VersionContrato = "1"`):

| Campo | Notas |
|---|---|
| `versionContrato` | `"1"`. Cambios incompatibles suben la versión; los aditivos no. |
| `id`, `referencia` | `referencia` = `PROD_ID` A+W (llave estable). |
| `descripcion`, `unidad` | Código de `UnidadMedida`. |
| `claveProdServSat`, `claveUnidadSat`, `objetoImp` | Nulos si el operador no los completó. |
| `tasaIvaTraslado`, `tasaRetencionIva`, `tasaRetencionIsr` | Nulo != 0. |
| `estatus`, `fechaBaja` | `Activo` / `Inactivo`; `fechaBaja` solo en baja. |
| `variantes[]` | `claveVariante`, `altoMm`, `anchoMm`, `espesorMm`, `composicion` (nulo = no informado), ordenadas por clave. |
| `version` | Concurrencia optimista; también viaja como `ETag`. |

```json
{
  "versionContrato": "1",
  "id": "0198a000-0000-7000-8000-000000000001",
  "referencia": "DEMO-P001",
  "descripcion": "PRODUCTO DEMO 001",
  "unidad": "M2",
  "claveProdServSat": null,
  "claveUnidadSat": "H87",
  "objetoImp": null,
  "tasaIvaTraslado": null,
  "tasaRetencionIva": null,
  "tasaRetencionIsr": null,
  "estatus": "Activo",
  "fechaBaja": null,
  "variantes": [
    { "claveVariante": "V1", "altoMm": null, "anchoMm": null, "espesorMm": 6, "composicion": "6mm" }
  ],
  "version": 1
}
```

**Endpoints** (base `/api/v1/datos-maestros/productos-aw`, todos `RequireAuthorization` en la API, errores como Problem Details):

| Método y ruta | Qué hace |
|---|---|
| `GET /{referencia}/contrato` | Contrato V1 por referencia (200 con `ETag`; 404 si no existe, sin revelar datos). |
| `POST /sincronizacion` | Barrido síncrono acotado (lotes del origen). `Idempotency-Key` obligatorio. 200 con resumen; 503 si el origen está apagado/sin configurar. |
| `POST /sincronizacion/{referencia}` | Reintento por fila. `Idempotency-Key` obligatorio; `If-Match` opcional (ADR-0012): versión vieja o producto manual => 409 `PRODUCTO_AW_CONFLICTO_VERSION` sin escribir; referencia ausente en origen => 404. |
| `GET /{id}/sincronizacion` | Estado por producto: `version` (+`ETag`) y `sincronizacion` (resultado, error, diferencias, hash, leído/aplicado, versiones de contrato/mapeo, crudos de origen). `sincronizacion = null` si nunca vino de A+W. 404 si el id no existe. |

**Permisos:** todo reutiliza `datos_maestros.productos-aw.gestionar` (el canónico de ADM-01/02, que ya protege el GET de lectura de productos A+W). **No se crea permiso nuevo ni seed/migración**: la tarea pide reutilizar canónicos y un permiso `sincronizar` separado no aporta hoy (misma audiencia: administradores del master). Si se quiere separar luego, se añade `datos_maestros.productos-aw.sincronizar` siguiendo el seed de clientes. Sin el permiso la API responde 403 (verificado en la API, no solo en la UI).

**Alcance por sucursal:** no aplica. Productos A+W es master cross-empresa sin `SucursalId` (ADR-0048 D5); ADR-0051 solo exige `SucursalScopeGuard` para datos scoped por sucursal, y el patrón de clientes de ADM-06 tampoco lo usa en sincronización. El control de acceso es únicamente el permiso.

## 10. Escenarios de fixtures (11)

Ubicación: `backend/tests/Integraciones.Aw.UnitTests/Productos/Fixtures/`. Cada archivo trae `escenario`, `descripcion`, `origen.vw_erp_articulo[]`, `estadoErpPrevio` y `resultadoEsperado`.

| Escenario | Qué fija |
|---|---|
| `nominal` | Alta con unidad conocida y una variante. |
| `dos-medidas-mismo-codigo` | 1 producto, 2 variantes. |
| `tratado-templado` | Código distinto, sin producto base. |
| `tratado-laminado` | Código distinto, composición `6mm+PVB+6mm`. |
| `unidad-desconocida` | `RequiereRevision` / `UNIDAD_SIN_EQUIVALENCIA`, sin persistir. |
| `duplicado` | Mismo `producto_ref` dos veces, upsert idempotente. |
| `baja` | `Inactivo` + `FechaBaja`, sin DELETE. |
| `baja-con-historico` | Bloquea uso nuevo, histórico consultable. |
| `conflicto-version` | `ConflictoVersion`, sin sobrescribir. |
| `campo-fiscal-ya-completado` | Clave SAT del operador no se pisa. |
| `medida-nula-no-es-cero` | Nulo se conserva nulo. |

El test `ProductosFixturesTests` valida solo la **forma** de los fixtures; el comportamiento se cubrirá en los pasos B–F con pruebas de dominio/servicio.

## 11. Pendientes con Millet / A+W

- **O1A-AW-MAP:** esquema verificado (ver §0). Pendientes de Millet: tipos de `BA_PRODUKTART` que entran al catálogo (`TiposExcluidos`), significado de `KZ_GESPERRT`, si `BA_STUKL` es la composición oficial, y quién resuelve conflictos con productos manuales. Todo en `Documentos/ADM07_Preguntas_para_Eliam.md`.
- **O1A-AW-INT:** lectura real vía Hybrid Connection.
- Confirmar con Millet: criterio exacto de baja en A+W, catálogo de unidades A+W y su equivalencia SAT, si un mismo código puede llegar con medidas distintas por lote.
- **PLATFORM-TODO(<AwProductosHybridConnection>):** el lector SQL (`AwProductosSqlOrigen`, solo `SELECT`, SQL Server 2016) **está construido y apagado por defecto**: `IntegracionesAw:Productos:OrigenHabilitado=true`, `Origen=Sql` y `ConnectionStrings:AwProductosDb` con TLS verificado (`Encrypt=True`, sin `TrustServerCertificate`). Falta O1A-AW-INT / A3 (ruta privada Azure → A+W y certificado TLS válido) y A4 (ventana para un barrido real). Con `Origen=Simulado` sigue el origen simulado (`AwProductosOrigenSimulado`, JSON con formato de fixtures). **Verificado hasta hoy:** las consultas del lector se ejecutaron en solo lectura contra `MILMAIN` (muestra acotada) y los tests unitarios cubren el mapeo; **no** se ha corrido un barrido real completo ni el adaptador de extremo a extremo. Filtro de tipos configurable: `TiposExcluidos` (decisión pendiente de Millet).

## 11.1 Validación real y puesta en marcha

**a) Prueba de humo (`aw-smoke`).** Con la VPN arriba, el dueño corre `! aw-smoke` (script fuera del repo, en `~/.local/bin`). Ejecuta `AwProductosSqlOrigenRealTests` (`Category=AwReal`) con el lector real contra A+W, solo lectura. Comprueba únicamente **estructura**: páginas completas salvo la última, cursor que avanza, `ProductoRef` único y numérico, todas las filas pasan por el mapper, composiciones con forma válida, medidas nunca 0, y lectura por referencia (inexistente → null; existente → la misma fila). Imprime solo agregados (filas, bajas, con variante, con composición, unidades normalizadas, errores del mapper). Variables opcionales: `AW_SMOKE_PAGINAS` (2 por defecto, máx. 5) y `AW_SMOKE_TIPOS_EXCLUIDOS` (coma-separado). Sin `AW_SMOKE_HOST/DB/USER/PASS` el test no hace nada y lo informa.

Variables opcionales (se pasan con `env`, p. ej. `! env AW_SMOKE_PAGINAS=5 AW_SMOKE_CURSOR=410000 aw-smoke`): `AW_SMOKE_PAGINAS` (1 a 5, default 2), `AW_SMOKE_CURSOR` (entero: arranca después de ese `BA_PRODUKT`; sirve para llegar a laminados y aislantes, que no están al inicio del catálogo) y `AW_SMOKE_TIPOS_EXCLUIDOS` (coma-separado).

**Evidencia (2026-10-01, solo lectura contra `MILMAIN`, `TrustServerCertificate` solo en el test).** Dos corridas de `aw-smoke` con 5 páginas de 50 productos (500 de 6,799, ~7 %): (1) inicio del catálogo: 250 filas, 92 bajas, 247 con variante, 1 con composición (vidrio plano cuya lista es de procesos), unidades `M2` 246 / `PZA` 2 / sin equivalencia 2, 0 errores del mapper; (2) desde `BA_PRODUKT` 410000 (`AW_SMOKE_CURSOR`): 250 filas, 17 bajas, 250 con variante, 249 con composición (todas con formato de espesores), unidades `M2` 250, 0 errores del mapper. Las corridas detectaron dos defectos ya corregidos: composición por descripción (procesos/kits) y la referencia `0` (registro nulo de A+W). **Alcance:** valida la estructura del lector con datos reales en una muestra; **no** es un barrido completo, ni TLS verificado, ni la integración de extremo a extremo (A3/A4).

**b) Activar el lector con Hybrid Connection.** `IntegracionesAw:Productos:OrigenHabilitado=true`, `IntegracionesAw:Productos:Origen=Sql`, `TamanoLote`, `SqlQueryTimeoutSeconds`, `TiposExcluidos` (decisión P1 de Millet) y `ConnectionStrings:AwProductosDb` desde Key Vault con `Encrypt=True` y **sin** `TrustServerCertificate`.

**c) Criterio de aceptación al llegar Hybrid (A3/A4).** El mismo test, sin `TrustServerCertificate`, desde un entorno con ruta válida y certificado TLS verificado (A3), más una ventana acordada para un barrido real (A4).

**d) Alcance.** El test de humo es opt-in y manual: no es integración continua ni prueba de extremo a extremo. Su fábrica con `TrustServerCertificate=true` vive solo en el test; el código de producción exige TLS verificado.

## 12. Límite conocido: sin ejecución durable

El sincronizador (`AwProductosSincronizador`) es síncrono y acotado: barrido por páginas o por referencia, devuelve un resumen (leídos, creados, actualizados, sin cambios, pendientes, conflictos, errores y errores por referencia con código y mensaje). No hay entidad de ejecución, worker/dispatcher ni cursor persistido: el estado por fila vive en `ProductoSincronizacionAw`, y los endpoints de D1 son síncronos (§9). Si el volumen real lo exige, se agrega ejecución durable siguiendo el patrón de clientes (`AwClientesEjecucion`).
