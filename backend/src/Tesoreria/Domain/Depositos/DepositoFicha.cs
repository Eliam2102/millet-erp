using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Depositos;

/// <summary>
/// Ficha de depósito de un corte de caja (Millet 23-oct): un movimiento bancario de
/// ingreso que cubre todo o parte del efectivo de la sesión. Un corte se cubre con
/// 1..N fichas; una ficha pertenece a un solo corte (índice único por movimiento).
/// </summary>
public sealed class DepositoFicha : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid DepositoConfirmacionId { get; private set; }
    public Guid MovimientoId { get; private set; }
    public decimal Importe { get; private set; }
    public DateOnly FechaFicha { get; private set; }
    public string? NumeroFicha { get; private set; }

    /// <summary>Obligatorio desde la segunda ficha del corte.</summary>
    public SoporteFicha? SoporteTipo { get; private set; }

    /// <summary>Folio del vale de caja o del corte parcial (referencia; el vale lo imprime otra tarea).</summary>
    public string? SoporteFolio { get; private set; }

    public Guid ConfirmadaPor { get; private set; }
    public DateTimeOffset ConfirmadaEn { get; private set; }

    private DepositoFicha() { }

    internal static DepositoFicha Crear(DepositoConfirmacion deposito, Guid movimientoId, decimal importe,
        DateOnly fecha, string? numero, SoporteFicha? tipo, string? folio, Guid usuarioId, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        EmpresaId = deposito.EmpresaId,
        DepositoConfirmacionId = deposito.Id,
        MovimientoId = movimientoId,
        Importe = importe,
        FechaFicha = fecha,
        NumeroFicha = numero,
        SoporteTipo = tipo,
        SoporteFolio = folio,
        ConfirmadaPor = usuarioId,
        ConfirmadaEn = ahora,
    };
}

public enum SoporteFicha : short
{
    ValeCaja = 1,
    CorteParcial = 2,
}
