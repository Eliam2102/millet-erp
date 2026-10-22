using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Domain.Apartados;

public sealed class LiberacionApartado : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid RequisicionId { get; private set; }
    public Guid LineaRequisicionId { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    public decimal Cantidad { get; private set; }
    public string Motivo { get; private set; } = null!;
    private LiberacionApartado() { }
    public LiberacionApartado(ApartadoRequisicion apartado, decimal cantidad, string motivo, Guid actorId, DateTimeOffset fecha)
        : base(Guid.CreateVersion7())
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 500)
            throw new BusinessRuleException("APARTADO_MOTIVO_REQUERIDO", "Indica el motivo de liberación, de hasta 500 caracteres.");
        if (actorId == Guid.Empty)
            throw new BusinessRuleException("APARTADO_ACTOR_REQUERIDO", "Se requiere el usuario que autoriza la liberación.");
        apartado.Liberar(cantidad);
        EmpresaId = apartado.EmpresaId;
        RequisicionId = apartado.RequisicionId;
        LineaRequisicionId = apartado.LineaRequisicionId;
        Cantidad = cantidad;
        Motivo = motivo.Trim();
        ActorId = actorId;
        Fecha = fecha;
    }
}
