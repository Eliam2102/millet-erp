using Millet.Almacen.Domain.Ports;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Adapters;

public sealed class AlmacenCentroCostoElegibilidadAdapter(IDim3ElegibilidadPort port) : ICentroCostoElegibilidadPort
{
    public async Task ValidarAsync(Guid centroCostoId, CancellationToken ct)
    {
        var estado = await port.EvaluarAsync(centroCostoId, aplicarAlcance: true, ct);
        if (estado == Dim3Elegibilidad.Valida) return;
        var motivo = estado switch
        {
            Dim3Elegibilidad.Inactiva => "está inactivo",
            Dim3Elegibilidad.NoExiste => "no existe",
            _ => "está fuera de su alcance",
        };
        throw new BusinessRuleException("CECO_INVALIDO", $"El centro de costo {motivo}; selecciona uno activo dentro de su alcance.");
    }
}
