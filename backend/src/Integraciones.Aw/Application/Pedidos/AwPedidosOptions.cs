using Millet.Facturacion.Domain.Facturas;

namespace Millet.Integraciones.Aw.Application.Pedidos;

/// <summary>
/// Opciones del flujo 2 — ingesta de pedidos en firme A+W → Facturación
/// (ADR-0048, doc integration/04 §4). Sección <c>IntegracionesAw:Pedidos</c>.
/// Independiente de <see cref="IntegracionesAwOptions"/> (flujo 1,
/// cotizaciones del Glass Agent) a propósito: BD distinta, timeouts propios.
///
/// <para>
/// Rediseño 2026-07-07 (doc 04 §4): la sucursal se resuelve contra el
/// catálogo (<c>compartido.sucursales.clave_aw</c>). FAC-ING-PR2: el canal
/// también — la vista emite el GRUPPE crudo de A+W en <c>canal_ventas</c> y
/// se machea contra <c>compartido.canales_venta.clave_aw</c> (el diccionario
/// <c>MapeoCanalVenta</c> fue retirado). La clase sigue llegando traducida a
/// nombre del enum <see cref="ComportamientoFiscal"/>; su diccionario queda
/// como <b>override opcional</b> que se consulta antes del
/// <c>Enum.TryParse</c>. Valor no resoluble → el reader devuelve
/// <c>null</c> y la ingesta cae a la bandeja de excepciones.
/// </para>
/// </summary>
public sealed class AwPedidosOptions
{
    public const string SectionName = "IntegracionesAw:Pedidos";

    /// <summary>Timeout por query (segundos). Mismo criterio que el flujo 1.</summary>
    public int SqlQueryTimeoutSeconds { get; set; } = 5;

    /// <summary>Timeout duro de OpenAsync (segundos) — fix E2E PR D del flujo 1.</summary>
    public int SqlConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Override opcional <c>clase</c> → <see cref="ComportamientoFiscal"/>;
    /// por defecto la vista ya entrega el nombre del enum (regla fija:
    /// exportación → <see cref="ComportamientoFiscal.ExportacionConCce"/>).
    /// </summary>
    public Dictionary<string, ComportamientoFiscal> MapeoComportamiento { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>unidad_medida</c> normalizada de A+W (M2/PZA/ML/KG) → clave unidad
    /// SAT (MTK/H87/…/KGM). La consume la auto-provisión de ProductoAw (PR4);
    /// el reader NO la aplica a las líneas (las claves SAT las pone el master).
    /// </summary>
    public Dictionary<string, string> MapeoUnidadSat { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["M2"] = "MTK",
        ["PZA"] = "H87",
        ["KG"] = "KGM",
        // ML (metro lineal) por confirmar con el sample fiscal (gap G6).
    };
}
