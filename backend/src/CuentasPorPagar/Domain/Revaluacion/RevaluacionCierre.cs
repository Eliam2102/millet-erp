using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Revaluacion;

/// <summary>
/// CXP-12: revaluación de cierre (no realizada) de los saldos por pagar en moneda extranjera. Un registro por empresa y mes.
/// Regla de Contabilidad (C-10): TC del último día del mes, diferencia a resultados y reversa automática el día 1 del periodo siguiente.
/// </summary>
public sealed class RevaluacionCierre : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    private readonly List<RevaluacionDetalle> _detalles = [];

    public Guid EmpresaId { get; set; }
    public int Anio { get; private set; }
    public int Mes { get; private set; }
    public DateOnly FechaCierre { get; private set; }
    public DateOnly FechaReversa { get; private set; }
    /// <summary>Suma de las diferencias positivas (el pasivo en pesos subió).</summary>
    public decimal TotalPerdida { get; private set; }
    /// <summary>Suma de las diferencias negativas, en positivo (el pasivo en pesos bajó).</summary>
    public decimal TotalUtilidad { get; private set; }
    public DateTimeOffset? ReversaPublicadaEn { get; private set; }
    public IReadOnlyCollection<RevaluacionDetalle> Detalles => _detalles.AsReadOnly();

    private RevaluacionCierre() { }

    public RevaluacionCierre(Guid empresaId, int anio, int mes, IEnumerable<RevaluacionDetalle> detalles) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId; Anio = anio; Mes = mes;
        FechaCierre = new DateOnly(anio, mes, DateTime.DaysInMonth(anio, mes));
        FechaReversa = FechaCierre.AddDays(1);
        foreach (var d in detalles) { d.Asignar(Id); _detalles.Add(d); }
        TotalPerdida = _detalles.Where(d => d.Diferencia > 0).Sum(d => d.Diferencia);
        TotalUtilidad = -_detalles.Where(d => d.Diferencia < 0).Sum(d => d.Diferencia);
    }

    public void MarcarReversaPublicada(DateTimeOffset ahora)
    {
        if (ReversaPublicadaEn is not null)
            throw new ConflictException("REVALUACION_YA_REVERTIDA", "La reversa de esta revaluación ya se publicó.");
        ReversaPublicadaEn = ahora;
    }
}

/// <summary>Una factura abierta en moneda extranjera revaluada: saldo en su moneda, TC de la factura y TC de cierre.</summary>
public sealed class RevaluacionDetalle
{
    public Guid Id { get; private set; }
    public Guid RevaluacionId { get; private set; }
    public Guid FacturaProveedorId { get; private set; }
    public Guid ProveedorId { get; private set; }
    /// <summary>Sucursal de la factura: la póliza de la revaluación va por proveedor y sucursal.</summary>
    public Guid SucursalId { get; private set; }
    public string Moneda { get; private set; } = string.Empty;
    public decimal Saldo { get; private set; }
    public decimal TcFactura { get; private set; }
    public decimal TcCierre { get; private set; }
    /// <summary>Saldo en pesos como está en libros (saldo × TC de la factura).</summary>
    public decimal ImporteLibros { get; private set; }
    /// <summary>Saldo en pesos al TC de cierre.</summary>
    public decimal ImporteCierre { get; private set; }
    /// <summary>Importe de cierre − libros: positivo = pérdida cambiaria no realizada, negativo = utilidad.</summary>
    public decimal Diferencia { get; private set; }

    private RevaluacionDetalle() { }

    public RevaluacionDetalle(Guid facturaId, Guid proveedorId, Guid sucursalId, string moneda, decimal saldo, decimal tcFactura, decimal tcCierre)
    {
        Id = Guid.CreateVersion7(); FacturaProveedorId = facturaId; ProveedorId = proveedorId; SucursalId = sucursalId; Moneda = moneda;
        Saldo = saldo; TcFactura = tcFactura; TcCierre = tcCierre;
        ImporteLibros = Redondear(saldo * tcFactura); ImporteCierre = Redondear(saldo * tcCierre);
        Diferencia = ImporteCierre - ImporteLibros;
    }

    internal void Asignar(Guid revaluacionId) => RevaluacionId = revaluacionId;

    private static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
