using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cancelaciones;

/// <summary>
/// M-235: cuántas firmas pide la cancelación de un comprobante según su tipo de operación
/// y su importe en MXN. Arriba del umbral (importe &gt; <see cref="UmbralMxn"/>) se aplican
/// <see cref="FirmasRequeridas"/>; sin regla que aplique basta una firma (comportamiento
/// de F6.5). La tabla nace vacía: la configura el Contador General.
/// </summary>
public sealed class ReglaFirmaCancelacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    /// <summary>Tipos de comprobante a los que puede aplicar una regla (nombre del tipo).</summary>
    public static readonly IReadOnlyList<string> TiposOperacion =
        ["FacturaVenta", "FacturaAnticipo", "NotaCredito", "ReciboPago", "CartaPorte"];

    public Guid EmpresaId { get; set; }

    /// <summary>Tipo de comprobante; <c>null</c> = cualquier tipo.</summary>
    public string? TipoComprobante { get; private set; }

    /// <summary>Importe en MXN (con impuestos) arriba del cual aplica la regla.</summary>
    public decimal UmbralMxn { get; private set; }

    /// <summary>1 o 2.</summary>
    public short FirmasRequeridas { get; private set; }

    private ReglaFirmaCancelacion() { }

    public static ReglaFirmaCancelacion Crear(Guid empresaId, string? tipoComprobante, decimal umbralMxn, short firmasRequeridas)
    {
        var tipo = string.IsNullOrWhiteSpace(tipoComprobante) ? null : tipoComprobante.Trim();
        if (tipo is not null && !TiposOperacion.Contains(tipo))
            throw new BusinessRuleException("CANCELACION_REGLA_TIPO_INVALIDO", $"El tipo de operación «{tipo}» no existe.");
        if (umbralMxn < 0)
            throw new BusinessRuleException("CANCELACION_REGLA_UMBRAL_INVALIDO", "El umbral no puede ser negativo.");
        if (firmasRequeridas is < 1 or > 2)
            throw new BusinessRuleException("CANCELACION_REGLA_FIRMAS_INVALIDAS", "Una cancelación pide una o dos firmas.");

        return new ReglaFirmaCancelacion
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            TipoComprobante = tipo,
            UmbralMxn = Math.Round(umbralMxn, 2, MidpointRounding.AwayFromZero),
            FirmasRequeridas = firmasRequeridas,
        };
    }

    /// <summary>Firmas que pide un comprobante: la mayor de las reglas que le aplican; sin regla, una.</summary>
    public static short FirmasPara(IEnumerable<ReglaFirmaCancelacion> reglas, string tipoComprobante, decimal importeMxn) =>
        reglas
            .Where(r => (r.TipoComprobante is null || r.TipoComprobante == tipoComprobante) && importeMxn > r.UmbralMxn)
            .Select(r => r.FirmasRequeridas)
            .DefaultIfEmpty((short)1)
            .Max();
}
