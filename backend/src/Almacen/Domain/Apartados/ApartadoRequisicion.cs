using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Apartados;

public sealed class ApartadoRequisicion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public Guid RequisicionId { get; private set; }
    public Guid LineaRequisicionId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public decimal Pendiente { get; private set; }
    public decimal CantidadLiberada { get; private set; }
    public string? MotivoLiberacion { get; private set; }
    private ApartadoRequisicion() { }
    public ApartadoRequisicion(Guid empresaId, Guid sucursalId, Guid rqId, Guid lineaId,
        Guid articuloId, decimal cantidad) : base(Guid.CreateVersion7())
    {
        if (cantidad <= 0) throw new BusinessRuleException("APARTADO_CANTIDAD_INVALIDA",
            "La cantidad apartada debe ser positiva.");
        EmpresaId = empresaId;
        SucursalId = sucursalId;
        RequisicionId = rqId;
        LineaRequisicionId = lineaId;
        ArticuloId = articuloId;
        Pendiente = cantidad;
    }
    public void Consumir(decimal cantidad)
    {
        if (cantidad <= 0) throw new BusinessRuleException("APARTADO_CONSUMO_INVALIDO",
            "La cantidad surtida debe ser positiva.");
        Pendiente = Math.Max(0m, Pendiente - cantidad);
    }
    public void Liberar() => Pendiente = 0m;

    /// <summary>Libera parte del apartado pendiente con motivo (86e3tferc); sin recorte silencioso.</summary>
    public void Liberar(decimal cantidad, string motivo)
    {
        if (cantidad <= 0) throw new BusinessRuleException("APARTADO_LIBERACION_INVALIDA",
            "La cantidad a liberar debe ser positiva.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new BusinessRuleException("APARTADO_MOTIVO_REQUERIDO",
            "Indica el motivo de la liberación.");
        if (cantidad > Pendiente) throw new BusinessRuleException("APARTADO_LIBERACION_EXCEDE",
            $"Sólo hay {Pendiente} apartado pendiente; no se puede liberar {cantidad}.");
        Pendiente -= cantidad;
        CantidadLiberada += cantidad;
        MotivoLiberacion = motivo.Trim();
    }
}
