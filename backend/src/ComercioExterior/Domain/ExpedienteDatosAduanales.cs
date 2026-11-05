using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;
public sealed partial class Expediente
{
    public void GuardarDatosAduanales(Guid partidaId, string fraccion, string unidad, Guid actor, DateTimeOffset fecha, bool revisar)
    {
        ExigirExportacion();
        var p = _partidas.SingleOrDefault(p => p.Id == partidaId && p.Estado != EstadoPartida.Liberada) ?? throw new EntityNotFoundException("CE_PARTIDA_NO_ENCONTRADA", "Seleccione una partida activa del embarque.");
        var relacionadas = _partidas.Where(x => x.PedidoFacturableId == p.PedidoFacturableId && x.Posicion == p.Posicion && x.Estado != EstadoPartida.Liberada).ToArray();
        // Una mercancía de la factura puede viajar en varios contenedores; sus datos aduanales son únicos.
        foreach (var x in relacionadas) x.GuardarDatosAduanales(fraccion, unidad, actor, fecha, revisar);
        VersionarEmbarque(actor); RevisionContenido++;
    }
}
