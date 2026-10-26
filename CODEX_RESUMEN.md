# FMT-0 · Kit común de PDF · continuación del 22-oct-2026

## Estado y alcance

Rama `feature/fmt0-kit-pdf`; HEAD inicial `8c52b0fdb3943e7b3cb7fef4e791caac8a1bef96`. Sin commit, push ni cambio de rama. Se leyeron completos y en orden la continuación, reglas comunes y FMT-0; también `CLAUDE.md`, `CONTRIBUTING.md` y las APIs prohibidas.

Al retomar esta sesión no había conflictos pendientes. Ya existían la corrección local de `backend/Millet.sln` y este resumen sin seguimiento; se conservaron y se verificaron contra el código. La base realmente integrada incluye `5120a920`, posterior a `534bcd0e`: por eso el diff contra la base indicada también enumera cambios ajenos de Datos Maestros. El diff contra `5120a920` permite aislar FMT-0. No se alteraron esos cambios ni Activos Fijos.

El kit ya estaba implementado en el WIP. La corrida anterior ya había corregido localmente el cierre faltante `EndProject` de `Documentos.UnitTests` en `backend/Millet.sln`, junto a la entrada de Activos Fijos. Se preservaron los dos proyectos y sus configuraciones. No se reescribió el kit.

**Migración: no aplica.** La ficha específica FMT-0 dice «No toques ... las entidades, las migraciones»; no cambia ningún DbContext ni define nombre de migración. No se ejecutó `dotnet ef migrations add`, no se generó una migración vacía ni se editó un ModelSnapshot. Las migraciones de Datos Maestros/Identidad ya integradas desde la base se conservaron.

## Inventario por archivo de FMT-0

Los siguientes archivos ya venían en el WIP y se revisaron, salvo la corrección de solución indicada:

| Archivo (rutas relativas a la raíz) | Resultado |
| --- | --- |
| `backend/src/Documentos/Millet.Documentos.csproj` | Biblioteca net10.0 independiente de módulos de negocio; QuestPDF y QRCoder. |
| `backend/Directory.Packages.props` | QRCoder 1.6.0 centralizado. |
| `backend/Millet.sln` | Biblioteca y pruebas registradas; **verificado en esta sesión** el `EndProject` que ya estaba corregido localmente. |
| `backend/src/Compras/Millet.Compras.csproj`, `backend/src/Facturacion/Millet.Facturacion.csproj` | Referencias al kit, aún sin consumir sus piezas. |
| `backend/src/Api/Program.cs` | Una llamada a `PdfLicencia.Inicializar(builder.Configuration["Pdf:LicenciaQuestPdf"])`. |
| `backend/src/Documentos/PdfLicencia.cs` | Inicialización protegida por lock, Community por omisión y Professional; rechaza valores inválidos o contradictorios. |
| `backend/src/Documentos/Modelos.cs` | Records de encabezado, título y totales; copias defensivas de colecciones/logo. |
| `backend/src/Documentos/PdfEstilos.cs` | Carta, margen 1.2 cm, Calibri con Arial alterna; colores y tamaños centralizados. |
| `backend/src/Documentos/ImporteConLetra.cs` | Español/inglés, MXN/USD, redondeo AwayFromZero, sin negativos, hasta 999,999,999,999.99. |
| `backend/src/Documentos/CfdiQr.cs` | URL con total 10.6 invariante y parámetros escapados; PNG QRCoder ECC M. |
| `backend/src/Documentos/Componentes/EncabezadoEmpresa.cs` | Logo o nombre, título bilingüe, datos fiscales a la derecha y marca opcional. |
| `backend/src/Documentos/Componentes/BloqueEtiquetado.cs` | Barra gris y campos no vacíos. |
| `backend/src/Documentos/Componentes/TablaRenglones.cs` | Columnas relativas, alineación, traducción y detalle opcionales; cabecera repetida. |
| `backend/src/Documentos/Componentes/BloqueTotales.cs` | Totales a la derecha y filas resaltadas. |
| `backend/src/Documentos/Componentes/PiePagina.cs` | Leyenda azul centrada y Página X de Y. |
| `backend/src/Documentos/Componentes/QrCfdi.cs` | QR de 2.75 cm. |
| `backend/src/Documentos/Componentes/BloqueTimbre.cs` | QR/fecha y tres cajas de sellos/cadena a 6 pt. |
| `backend/tests/Documentos.UnitTests/Millet.Documentos.UnitTests.csproj` | xUnit, FluentAssertions y logo como recurso embebido. |
| `backend/tests/Documentos.UnitTests/ImporteConLetraTests.cs` | Casos de la ficha, límites, singular/plural, millones y redondeo. |
| `backend/tests/Documentos.UnitTests/CfdiQrTests.cs` | Formato, cultura, escape, sello, firma PNG y entradas inválidas. |
| `backend/tests/Documentos.UnitTests/ModelosYLicenciaTests.cs` | Inmutabilidad, concurrencia/idempotencia y Professional aislado. |
| `backend/tests/Documentos.UnitTests/DocumentoMuestraTests.cs` | Todas las piezas, 60 partidas, con/sin logo, firma PDF y dos páginas renderizadas. |
| `backend/tests/Documentos.UnitTests/Recursos/logo-millet.jpg` | SHA-256 idéntico al original indicado: `2d71650c7d1b65cf802f7a7b0a65211ee35df7370f1851e4303d371587dabefd`. |
| `.gitignore` | Excluye `backend/tests/Documentos.UnitTests/Salidas/`. |
| `CODEX_RESUMEN.md` | **Reescrito en esta sesión:** entrega, API pública, decisiones y evidencia actualizada. |

## API pública para FMT-2 y FMT-4

Namespaces: `Millet.Documentos` y `Millet.Documentos.Componentes`. Todas las piezas implementan `IComponent` y se insertan con `.Component(...)` de QuestPDF.

- `PdfLicencia.Inicializar(string? licenciaQuestPdf = null)`: llamar desde el host antes de generar documentos. La primera llamada fija Community/Professional; posteriores llamadas iguales no hacen nada y configuraciones contradictorias fallan. El kit no depende de IConfiguration.
- `PdfEstilos.ConfigurarPagina(PageDescriptor pagina)`: aplica carta, margen y fuente. Constantes públicas: `GrisBarra`, `RojoMillet`, `AzulMarca`, `Blanco`, `MargenCm`, `Texto`, `Titulo`, `Detalle`, `Timbre`, `LogoAnchoCm`, `QrCm`, `EspaciadoCelda`.
- `EmpresaEncabezado(string razonSocial, string? nombreComercial, string rfc, string regimenFiscal, IEnumerable<string> domicilioLineas, byte[]? logo = null)`: régimen y domicilio ya formateados por el consumidor.
- `DocumentoTitulo(string TituloEs, string? TituloEn, string SerieFolio, DateTime Fecha, string? Marca = null)`.
- `EncabezadoEmpresa(EmpresaEncabezado empresa, DocumentoTitulo documento)`: colocar en `pagina.Header()` para repetirlo.
- `CampoEtiquetado(string Etiqueta, string? Valor)` y `BloqueEtiquetado(string titulo, IEnumerable<CampoEtiquetado> campos)`.
- `ColumnaTabla<T>(string EncabezadoEs, Func<T,string?> Valor, float AnchoRelativo = 1, AlineacionColumna Alineacion = Izquierda, string? EncabezadoEn = null)`. Alineaciones: `Izquierda`, `Centro`, `Derecha`.
- `TablaRenglones<T>(IEnumerable<T> renglones, IEnumerable<ColumnaTabla<T>> columnas, Func<T,string?>? detalle = null)`: requiere una columna como mínimo y anchos positivos finitos; `detalle` vacío se omite.
- `RenglonTotal(string Etiqueta, decimal Importe, string Moneda, bool Resaltado = false)`, `Totales(IEnumerable<RenglonTotal> renglones)` y `BloqueTotales(Totales totales)`: el consumidor decide orden, cálculo y monedas.
- `PiePagina(string? leyenda = null)`: colocar en `pagina.Footer()`; así aparece en todas las páginas.
- `ImporteConLetra.Espanol(decimal importe, string moneda)` / `.Ingles(...)`: devuelven mayúsculas con centavos; moneda ajena a MXN/USD o importe fuera del rango producen excepción clara.
- `CfdiQr.Url(string uuid, string rfcEmisor, string rfcReceptor, decimal total, string selloCfdi)`; `.Png(string url, int pixelesPorModulo = 5)`. Total no negativo con máximo 10 enteros y 6 decimales; sello de al menos 8 caracteres. La función arma la URL; no consulta al SAT ni acredita un timbrado.
- `QrCfdi(string url, int pixelesPorModulo = 5)`.
- `BloqueTimbre(string urlQr, string fechaTimbrado, string selloCfdi, string selloSat, string cadenaOriginal)`: fecha ya formateada por el consumidor; conserva el bloque completo mediante `ShowEntire()`.

### Ejemplo de composición

```csharp
using Millet.Documentos;
using Millet.Documentos.Componentes;
using QuestPDF.Fluent;

// En el host ya se inicializó la licencia.
var empresa = new EmpresaEncabezado("EMPRESA FICTICIA", "DEMO",
    "XAXX010101000", "601-General de Ley Personas Morales",
    ["DOMICILIO FICTICIO"]);
var titulo = new DocumentoTitulo("FACTURA DE EJEMPLO", "SAMPLE INVOICE",
    "DEMO 1", new DateTime(2026, 10, 22), "BORRADOR – SIN VALIDEZ FISCAL");
var url = CfdiQr.Url("00000000-0000-0000-0000-000000000001",
    empresa.Rfc, "XAXX010101000", 1m, "SELLOFICTICIO12345678");
var pdf = Document.Create(doc => doc.Page(page =>
{
    PdfEstilos.ConfigurarPagina(page);
    page.Header().Component(new EncabezadoEmpresa(empresa, titulo));
    page.Content().Column(col =>
    {
        col.Spacing(6);
        col.Item().Component(new BloqueEtiquetado("CLIENTE/CUSTOMER:",
            [new("Nombre:", "CLIENTE FICTICIO")]));
        col.Item().Component(new TablaRenglones<string>(["ARTÍCULO FICTICIO"],
            [new("Descripción", x => x, EncabezadoEn: "Description")],
            _ => "ITEM.1 REF: DEMO"));
        col.Item().Component(new BloqueTotales(new Totales(
            [new("Total", 1m, "MXN", true)])));
        col.Item().Text(ImporteConLetra.Espanol(1m, "MXN"));
        col.Item().Component(new BloqueTimbre(url, "22/10/2026 12:00:00",
            "SELLO FICTICIO", "SELLO SAT FICTICIO", "CADENA FICTICIA"));
    });
    page.Footer().Component(new PiePagina(
        "Este documento es una representación impresa de un CFDI"));
})).GeneratePdf();
```

El ejemplo completo con 60 partidas y recurso de logo está en `DocumentoMuestraTests.Crear`.

## Verificación actual del 22-oct

- **Solución completa compilada**, `dotnet build Millet.sln -warnaserror --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`: exit 0, **cero advertencias y cero errores**, 7 min 16.91 s. Incluye API, Documentos, Compras, Facturación y todos los proyectos de pruebas. Registro: `build-restaurado-actual.log`.
- **Facturación: 588 pruebas, 0 fallidas, 0 omitidas**, ejecutadas en proceso con xUnit; registro `facturacion-tests-actual.log`.
- Estructura `Project`/`EndProject` de toda la solución validada con aserciones y `git diff --check` sin errores.
- Proyecto de Documentos y sus pruebas compilados con analizadores del repositorio. Compras compiló con `-warnaserror --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`: cero advertencias y cero errores. Su suite ejecutó **755 pruebas, 0 fallidas, 0 omitidas** con el runner xUnit en proceso.
- `dotnet test tests/Documentos.UnitTests/Millet.Documentos.UnitTests.csproj --no-restore`: VSTest abortó al abrir su socket local (`SocketException (13): Permission denied`). No se reporta ese comando como exitoso.
- Alternativa: `XunitFrontController` en proceso, con el runner xUnit ya disponible en NuGet; **38 ejecutadas, 0 fallidas, 0 omitidas (repetidas en esta sesión)**. Se reutilizó el runner temporal documentado en Salidas/verificacion, sin añadir dependencias o herramientas al producto. Para Compras y Facturación se añadió al runtime temporal el framework Microsoft.AspNetCore.App y se ejecutó el runner desde el directorio bin de cada suite: las pruebas de auditoría buscan fuentes a partir de AppContext.BaseDirectory. Los intentos previos con errores de carga/ruta son fallos del runner y se conservan como diagnóstico; la ejecución corregida es `compras-tests-corregido-actual.log`.
- Muestras regeneradas en esta sesión: `backend/tests/Documentos.UnitTests/Salidas/muestra-kit.pdf` y `muestra-kit-sin-logo.pdf`, ignoradas por Git. Datos ficticios, marca de borrador y logo autorizado por la ficha.
- Ambas muestras: firma `%PDF`, dos páginas carta (612 × 792 pt), 60 artículos, cabeceras de tabla y pies repetidos, campo vacío omitido, total 2,898.04 MXN, importes ES/EN y timbre comprobados extrayendo texto con pypdf del runtime disponible. PdfPig no está en las dependencias y no se agregó.
- La configuración verificada usa Calibri con Arial alterna, conforme al fallback requerido.
- Revisión visual de las cuatro páginas generadas por QuestPDF y render Poppler del PDF con logo: sin recortes ni superposiciones visibles. Comparación con las dos páginas del original: se conserva la distribución general de logo/datos a la derecha, barras, totales y pie. Se respeta el texto blanco de barras de la ficha aunque la referencia usa texto oscuro; no es una copia exacta de la factura final.

### Comandos y registros reproducibles

Todos los registros están ignorados en `backend/tests/Documentos.UnitTests/Salidas/verificacion/`.

- Restauración offline de la solución: `dotnet restore Millet.sln --source /private/tmp/fmt0-nuget-vacio -p:NuGetAudit=false --disable-parallel -m:1 -nr:false` (desde backend), **exit 0**; paquetes ya instalados, sin cambios en versiones declaradas. Registro: `restore-serial-actual.log`.
- Los intentos previos sin restauración completa detectaron MSB3277 por referencias incompatibles de System.Memory.Data 6.0.0/10.0.3 en salidas locales; **la restauración de la solución y compilación posterior resolvieron el conflicto**; no se alteraron los proyectos ajenos para ocultarlo. También se cancelaron intentos paralelos de MSBuild al repetir la verificación en serie.
- `test-actual.log`: VSTest abortado por socket restringido.
- Repetición final sobre las salidas de la compilación restaurada: `xunit-final-actual.log`: **38/38**; `compras-tests-post-restore-actual.log`: **755/755**; `facturacion-tests-actual.log`: **588/588**. Total: **1,381 pruebas, 0 fallidas y 0 omitidas**.
- `muestra-kit-actual.txt` y `muestra-kit-sin-logo-actual.txt`: extracción actual del texto y comprobaciones independientes con pypdf.

## Límites y siguiente revisión

No se cambiaron los generadores actuales ni sus constructores estáticos, como exige la ficha. **Límite de transición:** esos constructores siguen asignando `LicenseType.Community`; pueden sobreescribir Professional cuando se utilicen. FMT-2/FMT-4 deben completar la centralización al sustituir esos generadores. La prueba Professional verifica el inicializador aislado, no corrige esa interacción preexistente.

No se agregaron cambios de producto en esta sesión: el kit ya cumplía la ficha y se conservó la corrección local heredada de la solución. Se reescribió este resumen y se regeneraron únicamente salidas de pruebas/obj/bin ignoradas. HEAD final: `8c52b0fdb3943e7b3cb7fef4e791caac8a1bef96`, igual al inicial. Estado final verificado: ` M backend/Millet.sln` y `?? CODEX_RESUMEN.md`.

No hay cambios de frontend, entidades, base de datos ni Activos Fijos. No se probaron integración con Docker, timbrado real, impresión física ni despliegue. La aceptación final visual de Claude y la integración del kit en OC/factura siguen pendientes por diseño.


## Siguiente acción recomendada

Claude puede revisar el diff de FMT-0 contra `5120a920` y las muestras ignoradas, y después integrar el kit en los rediseños FMT-2/FMT-4. La aceptación visual de Claude sigue **Por confirmar**. No se hizo commit, push ni cambio de rama.
