using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Entidad catálogo (F3-PR2) que mapea pares
/// <c>(regimen_proveedor, regimen_articulo)</c> a tasas de IVA y de
/// retención ISR (decisión C3 del diseño). Lookup desde el motor de
/// impuestos v1 para calcular impuestos por línea según los regímenes
/// fiscales del proveedor y del artículo.
///
/// Tabla <c>compras.regimenes_fiscales_articulo</c>. PK = id GUID;
/// UNIQUE por <c>(regimen_proveedor, regimen_articulo)</c>.
///
/// <para>
/// **Reemplazo del motor v0 deferred**: F3-PR2 introduce esta tabla +
/// el catálogo seed pero NO reemplaza los handlers actuales que usan
/// <c>CalculadorImpuestosV0</c> (IVA 16% fijo). El reemplazo completo
/// del motor en handlers es trabajo significativo que se aborda en un
/// PR follow-up dedicado (cambios cross-cutting en
/// <c>AgregarLineaManual</c>, <c>ActualizarLinea</c>,
/// <c>AdjuntarDocumento</c>, etc.).
/// </para>
/// </summary>
public sealed class RegimenFiscalArticulo : BaseEntity, IAuditable
{
    /// <summary>Código del régimen fiscal del proveedor (ej. "GENERAL", "RESICO").</summary>
    public string RegimenProveedor { get; private set; } = string.Empty;

    /// <summary>Código del régimen fiscal aplicable al artículo (ej. "GENERAL", "EXENTO", "SERVICIO_PROFESIONAL").</summary>
    public string RegimenArticulo { get; private set; } = string.Empty;

    /// <summary>Tasa de IVA aplicable, decimal 0.00..1.00 (16% = 0.16).</summary>
    public decimal IvaPorcentaje { get; private set; }

    /// <summary>Tasa de retención ISR aplicable, decimal 0.00..1.00; null si no aplica.</summary>
    public decimal? RetencionIsrPorcentaje { get; private set; }

    public DateTimeOffset VigenteDesde { get; private set; }

    /// <summary>Fin de vigencia; null = abierto.</summary>
    public DateTimeOffset? VigenteHasta { get; private set; }

    public bool Activo { get; private set; }

    private RegimenFiscalArticulo() { }

    public RegimenFiscalArticulo(
        Guid id,
        string regimenProveedor,
        string regimenArticulo,
        decimal ivaPorcentaje,
        decimal? retencionIsrPorcentaje,
        DateTimeOffset vigenteDesde,
        DateTimeOffset? vigenteHasta = null,
        bool activo = true) : base(id)
    {
        if (string.IsNullOrWhiteSpace(regimenProveedor) || regimenProveedor.Length > 60)
        {
            throw new BusinessRuleException(
                "REGIMEN_PROVEEDOR_INVALIDO",
                "RegimenProveedor debe tener 1-60 caracteres.");
        }
        if (string.IsNullOrWhiteSpace(regimenArticulo) || regimenArticulo.Length > 60)
        {
            throw new BusinessRuleException(
                "REGIMEN_ARTICULO_INVALIDO",
                "RegimenArticulo debe tener 1-60 caracteres.");
        }
        if (ivaPorcentaje < 0m || ivaPorcentaje > 1m)
        {
            throw new BusinessRuleException(
                "REGIMEN_IVA_FUERA_DE_RANGO",
                "IvaPorcentaje debe estar entre 0 y 1.");
        }
        if (retencionIsrPorcentaje is decimal r && (r < 0m || r > 1m))
        {
            throw new BusinessRuleException(
                "REGIMEN_ISR_FUERA_DE_RANGO",
                "RetencionIsrPorcentaje debe estar entre 0 y 1.");
        }

        RegimenProveedor = regimenProveedor;
        RegimenArticulo = regimenArticulo;
        IvaPorcentaje = ivaPorcentaje;
        RetencionIsrPorcentaje = retencionIsrPorcentaje;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        Activo = activo;
    }
}
