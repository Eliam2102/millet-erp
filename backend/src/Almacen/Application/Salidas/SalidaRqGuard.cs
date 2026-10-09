using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Salidas;

internal static class SalidaRqGuard
{
    public static void Validar(RequisicionLectura? rq, IReadOnlyList<RegistrarSalidaLineaInput> lineas)
    {
        if (rq is null)
            throw new BusinessRuleException("SALIDA_RQ_NO_ENCONTRADA", "La requisición no existe; selecciona una requisición autorizada.");
        if (rq.Estado is not ("Autorizada" or "EnSurtido"))
            throw new BusinessRuleException("SALIDA_RQ_NO_AUTORIZADA", $"La requisición está {EstadoDocumentoTexto.Describir(rq.Estado)}; no se puede entregar contra ella.");
        foreach (var linea in lineas)
        {
            if (linea.LineaRqId is null || linea.LineaRqId == Guid.Empty)
                throw new BusinessRuleException("SALIDA_LINEA_RQ_REQUERIDA", "Cada artículo de la salida debe indicar su línea de requisición.");
            if (!rq.Lineas.Any(l => l.LineaId == linea.LineaRqId && l.ArticuloId == linea.ArticuloId))
                throw new BusinessRuleException("SALIDA_LINEA_RQ_INVALIDA", "El artículo no corresponde a la línea de requisición seleccionada.");
        }
        foreach (var grupo in lineas.GroupBy(l => l.LineaRqId))
        {
            var disponible = rq.Lineas.Single(l => l.LineaId == grupo.Key).CantidadDisponibleEntregar;
            if (grupo.Sum(l => l.Cantidad) > disponible)
                throw new BusinessRuleException("SALIDA_EXCEDE_AUTORIZADO", $"La cantidad total a entregar supera lo disponible autorizado ({disponible}) en la línea de requisición.");
        }
    }
}
