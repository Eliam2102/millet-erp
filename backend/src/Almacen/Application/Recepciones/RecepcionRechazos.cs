using FluentValidation;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// G-10: lo rechazado en el acto no entra al inventario ni cuenta como recibido (el pendiente sigue en la OC);
/// se conserva como <see cref="RecepcionRechazo"/> con su motivo y evidencia.
/// </summary>
internal static class RecepcionRechazos
{
    /// <summary>Reglas de la partida compartidas por las dos variantes: rechazo con motivo y evidencia obligatoria en dañado o rechazado.</summary>
    public static void ReglasDeCondicion(AbstractValidator<RegistrarRecepcionLineaInput> linea)
    {
        linea.RuleFor(l => l.CantidadRechazada).GreaterThanOrEqualTo(0).WithMessage("La cantidad rechazada no puede ser negativa.");
        linea.RuleFor(l => l.MotivoRechazo).NotEmpty().When(l => l.CantidadRechazada > 0).WithMessage("Indica el motivo del rechazo.");
        linea.RuleFor(l => l.MotivoRechazo!).MaximumLength(500).When(l => l.MotivoRechazo is not null);
        linea.RuleFor(l => l.EvidenciaBlobRef).NotEmpty().When(l => l.CantidadDanada > 0 || l.CantidadRechazada > 0)
            .WithMessage("Adjunta una foto o documento como evidencia del daño o del rechazo.");
        linea.RuleFor(l => l.EvidenciaBlobRef!).MaximumLength(500).When(l => l.EvidenciaBlobRef is not null);
    }

    /// <summary>Deja en la recepción solo las partidas con algo aceptado y devuelve las que traen rechazo.</summary>
    public static (IReadOnlyList<RegistrarRecepcionLineaInput> Aceptadas, IReadOnlyList<RegistrarRecepcionLineaInput> Rechazadas) Separar(
        IReadOnlyList<RegistrarRecepcionLineaInput> lineas)
    {
        var aceptadas = lineas.Where(l => l.Cantidad > 0).ToList();
        if (aceptadas.Count == 0)
            throw new BusinessRuleException("RECEPCION_TODO_RECHAZADO",
                "Todas las partidas se rechazaron en el acto: no hay nada que recibir. Avisa al proveedor y deja la orden pendiente.");
        return (aceptadas, lineas.Where(l => l.CantidadRechazada > 0).ToList());
    }

    public static void Registrar(AlmacenDbContext db, Guid empresaId, Guid recepcionId, OcLectura oc, IReadOnlyList<RegistrarRecepcionLineaInput> rechazadas)
    {
        foreach (var r in rechazadas)
        {
            var lineaOc = oc.Lineas.FirstOrDefault(l => l.LineaId == r.LineaOcId && l.ArticuloId == r.ArticuloId)
                ?? throw new BusinessRuleException("RECEPCION_RECHAZO_LINEA_INVALIDA", "La partida rechazada no corresponde a una línea de la orden de compra.");
            db.RecepcionRechazos.Add(new RecepcionRechazo(Guid.CreateVersion7(), empresaId, recepcionId, oc.Id, lineaOc.LineaId, r.ArticuloId,
                r.CantidadRechazada, string.IsNullOrWhiteSpace(r.UnidadCapturada) ? lineaOc.UnidadMedida : r.UnidadCapturada,
                r.MotivoRechazo ?? "", r.EvidenciaBlobRef ?? ""));
        }
    }
}
