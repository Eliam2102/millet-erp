using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Conteos;

/// <summary>
/// Recuento sucesivo de una <see cref="LineaConteo"/> (F7-PR2, A7).
/// Cuando la captura inicial excede el umbral de variación, se exige
/// al menos un recuento; la línea no se puede aprobar sin él. Cada
/// recuento incrementa <see cref="Secuencia"/>.
/// </summary>
public sealed class RecuentoConteo : BaseEntity
{
    public Guid LineaConteoId { get; private set; }
    public int Secuencia { get; private set; }
    public decimal CantidadRecontada { get; private set; }
    public Guid CapturadoPor { get; private set; }
    public DateTimeOffset CapturadoAt { get; private set; }

    internal RecuentoConteo() { }

    public RecuentoConteo(
        Guid id,
        Guid lineaConteoId,
        int secuencia,
        decimal cantidadRecontada,
        Guid capturadoPor) : base(id)
    {
        if (lineaConteoId == Guid.Empty)
            throw new BusinessRuleException("RECUENTO_SIN_LINEA",
                "El recuento requiere línea padre.");
        if (secuencia <= 0)
            throw new BusinessRuleException("RECUENTO_SECUENCIA_INVALIDA",
                "La secuencia debe ser positiva.");
        if (cantidadRecontada < 0)
            throw new BusinessRuleException("RECUENTO_CANT_NEGATIVA",
                "La cantidad recontada no puede ser negativa.");
        if (capturadoPor == Guid.Empty)
            throw new BusinessRuleException("RECUENTO_SIN_USUARIO",
                "El recuento requiere usuario.");

        LineaConteoId = lineaConteoId;
        Secuencia = secuencia;
        CantidadRecontada = cantidadRecontada;
        CapturadoPor = capturadoPor;
        CapturadoAt = DateTimeOffset.UtcNow;
    }
}
