using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Conciliacion;

/// <summary>
/// Corrida de la conciliación diaria de un almacén de MP (A4.6): existencia teórica del ERP contra el físico que reporta A+W
/// o el conteo. Sin tolerancia: toda diferencia queda en <see cref="DiferenciaConciliacion"/>. Tabla
/// <c>almacen.conciliaciones_diarias</c>; un día sin diferencias también deja su corrida (A4.6-a).
/// </summary>
public sealed class ConciliacionDiaria : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const string FuenteAw = "AW";
    public const string FuenteConteo = "CONTEO";

    public Guid EmpresaId { get; set; }
    public Guid AlmacenId { get; private set; }
    public DateOnly Fecha { get; private set; }
    public string Fuente { get; private set; } = string.Empty;
    public int LineasComparadas { get; private set; }
    public int Diferencias { get; private set; }
    public Guid EjecutadaPor { get; private set; }

    private ConciliacionDiaria() { }

    public ConciliacionDiaria(Guid empresaId, Guid almacenId, DateOnly fecha, string fuente, int lineasComparadas, int diferencias,
        Guid ejecutadaPor) : base(Guid.CreateVersion7())
    {
        if (fuente is not (FuenteAw or FuenteConteo))
            throw new BusinessRuleException("CONCILIACION_FUENTE_INVALIDA", $"La fuente del físico debe ser {FuenteAw} o {FuenteConteo}.");
        EmpresaId = empresaId; AlmacenId = almacenId; Fecha = fecha; Fuente = fuente;
        LineasComparadas = lineasComparadas; Diferencias = diferencias; EjecutadaPor = ejecutadaPor;
    }
}

/// <summary>
/// Diferencia de una corrida por hoja/medida/lote (no por pedido, JUNTA-07). Si faltó pieza es una <see cref="Faltante"/>: la
/// salida no planificada, con su movimiento ya descontado. Si sobró, queda como <see cref="Sobrante"/> sin movimiento. Nace
/// <see cref="Pendiente"/> y la mesa de corte la aclara con un motivo; aclarar NO descuenta otra vez.
/// Cantidades en hojas si la línea tiene medida (<see cref="AreaHojaM2"/>), en la unidad base si no.
/// </summary>
public sealed class DiferenciaConciliacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const string Faltante = "FALTANTE";
    public const string Sobrante = "SOBRANTE";
    public const string Pendiente = "PENDIENTE";
    public const string Aclarada = "ACLARADA";

    public Guid EmpresaId { get; set; }
    public Guid ConciliacionId { get; private set; }
    public Guid AlmacenId { get; private set; }
    public DateOnly Fecha { get; private set; }
    public Guid ArticuloId { get; private set; }
    public Guid? ProductoAwVarianteId { get; private set; }
    public string? Lote { get; private set; }
    public decimal? AreaHojaM2 { get; private set; }
    public decimal Teorico { get; private set; }
    public decimal Fisico { get; private set; }
    public string Sentido { get; private set; } = string.Empty;
    public decimal CostoUnitarioMxn { get; private set; }
    public decimal ValorMxn { get; private set; }
    public Guid? MovimientoId { get; private set; }
    public string? FolioMovimiento { get; private set; }
    public string Estado { get; private set; } = Pendiente;
    public string? MotivoAclaracion { get; private set; }
    public Guid? AclaradaPor { get; private set; }
    public DateTimeOffset? AclaradaAt { get; private set; }

    /// <summary>Físico menos teórico; negativo = salida no planificada.</summary>
    public decimal Diferencia => Fisico - Teorico;

    private DiferenciaConciliacion() { }

    public DiferenciaConciliacion(Guid empresaId, Guid conciliacionId, Guid almacenId, DateOnly fecha, Guid articuloId,
        Guid? varianteId, string? lote, decimal? areaHojaM2, decimal teorico, decimal fisico, decimal costoUnitarioMxn,
        decimal valorMxn, Guid? movimientoId, string? folio) : base(Guid.CreateVersion7())
    {
        if (teorico == fisico)
            throw new BusinessRuleException("DIFERENCIA_SIN_DIFERENCIA", "No hay diferencia que registrar.");
        EmpresaId = empresaId; ConciliacionId = conciliacionId; AlmacenId = almacenId; Fecha = fecha; ArticuloId = articuloId;
        ProductoAwVarianteId = varianteId; Lote = lote; AreaHojaM2 = areaHojaM2; Teorico = teorico; Fisico = fisico;
        Sentido = fisico < teorico ? Faltante : Sobrante;
        CostoUnitarioMxn = costoUnitarioMxn; ValorMxn = valorMxn; MovimientoId = movimientoId; FolioMovimiento = folio;
    }

    public void Aclarar(Guid usuarioId, string motivo)
    {
        if (Estado != Pendiente)
            throw new BusinessRuleException("DIFERENCIA_YA_ACLARADA", "La diferencia ya fue aclarada.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("DIFERENCIA_SIN_USUARIO", "Aclarar requiere usuario.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            throw new BusinessRuleException("DIFERENCIA_MOTIVO_INVALIDO", "El motivo es obligatorio (hasta 500 caracteres).");
        Estado = Aclarada; MotivoAclaracion = motivo.Trim(); AclaradaPor = usuarioId; AclaradaAt = DateTimeOffset.UtcNow;
    }
}
