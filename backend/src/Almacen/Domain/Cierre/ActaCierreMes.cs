using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Cierre;

/// <summary>
/// Acta del cierre mensual (86e3tac2y, tabla <c>almacen.actas_cierre</c>): una por periodo cerrado, creada en la misma
/// transacción del cierre en estado «por revisar». La Dirección de Administración la marca revisada (usuario y fecha). El contenido
/// (totales por almacén, conteos y vales atendidos) es una foto en JSON; la bitácora de auditoría registra quién la revisó.
/// </summary>
public sealed class ActaCierreMes : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const string PorRevisar = "POR_REVISAR";
    public const string Revisada = "REVISADA";

    public Guid EmpresaId { get; set; }
    public Guid PeriodoCerradoId { get; private set; }
    public int Anio { get; private set; }
    public int Mes { get; private set; }
    public Guid EjecutadoPor { get; private set; }
    public DateTimeOffset EjecutadoAt { get; private set; }
    public string Estado { get; private set; } = PorRevisar;
    public Guid? RevisadaPor { get; private set; }
    public DateTimeOffset? RevisadaAt { get; private set; }
    public string ContenidoJson { get; private set; } = "{}";

    private ActaCierreMes() { }

    public ActaCierreMes(PeriodoCerrado periodo, string contenidoJson) : base(Guid.CreateVersion7())
    {
        EmpresaId = periodo.EmpresaId;
        PeriodoCerradoId = periodo.Id;
        Anio = periodo.Anio;
        Mes = periodo.Mes;
        EjecutadoPor = periodo.CerradoPor;
        EjecutadoAt = periodo.CerradoAt;
        ContenidoJson = contenidoJson;
    }

    public void MarcarRevisada(Guid usuarioId)
    {
        if (Estado == Revisada)
            throw new BusinessRuleException("ACTA_YA_REVISADA", "El acta ya está marcada como revisada.");
        Estado = Revisada;
        RevisadaPor = usuarioId;
        RevisadaAt = DateTimeOffset.UtcNow;
    }
}
