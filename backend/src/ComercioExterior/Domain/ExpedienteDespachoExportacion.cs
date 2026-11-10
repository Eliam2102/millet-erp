using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Domain;

public sealed partial class Expediente
{
    public void AprobarDespacho(Guid id, bool parcial, Guid actor, DateTimeOffset fecha, string nivel, string motivo, string? suplencia)
    {
        ExigirExportacion();
        if (actor == Guid.Empty || PreparadoresEmbarque.Contains(actor) || _partidas.Any(p => p.ContenedorId == id && p.ConfirmadoPorId == actor))
            throw new BusinessRuleException("CE_SALIDA_SEPARACION_FUNCIONES", "La autorización requiere a otra persona distinta de quien preparó el embarque.");
        Contenedor(id).AutorizarDespacho(parcial, new(actor, fecha, nivel, MotivoValido(motivo), suplencia, VersionEmbarque));
        RevisionContenido++;
    }

    public bool PrepararFacturaExportacion(Guid id, IReadOnlyCollection<PedidoEmbarcable> pedidos, DateTimeOffset ahora)
    {
        ExigirExportacion();
        var contenedor = Contenedor(id);
        ValidarPreparacionDespacho(id, pedidos);
        if (!contenedor.PrepararFactura(ahora)) return false;
        RevisionContenido++;
        return true;
    }

    public void DespacharExportacion(Guid id, IReadOnlyCollection<PedidoEmbarcable> pedidos, bool facturasVigentes, DateOnly fecha, DateOnly hoy)
    {
        ExigirExportacion();
        var contenedor = Contenedor(id);
        ValidarPreparacionDespacho(id, pedidos);
        if (contenedor.PreparadoParaFacturarEn is null)
            throw new BusinessRuleException("CE_PREPARACION_REQUERIDA", "Prepare la salida facturable antes de registrar el despacho.");
        if (!facturasVigentes && contenedor.LeerAutorizacion(false)?.VersionEmbarque != VersionEmbarque)
            throw new BusinessRuleException("CE_FACTURA_DESPACHO_REQUERIDA", "Cada salida del contenedor requiere factura timbrada vigente o una excepción autorizada.");
        contenedor.Embarcar(fecha, hoy);
        RevisionContenido++;
    }

    private void ValidarPreparacionDespacho(Guid id, IReadOnlyCollection<PedidoEmbarcable> pedidos)
    {
        var partidas = _partidas.Where(p => p.ContenedorId == id && p.Estado != EstadoPartida.Liberada).ToArray();
        if (partidas.Length == 0)
            throw new BusinessRuleException("CE_EMBARQUE_SIN_PARTIDAS", "Asigne partidas antes de preparar el contenedor.");
        var bloqueo = BloqueoPreparacionFacturable(partidas);
        if (bloqueo.Codigo is not null)
            throw new BusinessRuleException(bloqueo.Codigo, bloqueo.Motivo!);
        var parcial = false;
        foreach (var grupo in partidas.GroupBy(p => p.PedidoFacturableId))
        {
            var pedido = pedidos.SingleOrDefault(p => p.Id == grupo.Key);
            if (pedido is null || grupo.Any(p => p.EstadoActual(pedido.VersionOrigen) != EstadoPartida.Vigente))
                throw new BusinessRuleException("CE_PARTIDA_REQUIERE_REVISION", "Revise las partidas contra la versión vigente del pedido.");
            parcial |= pedido.Lineas.Any(linea =>
                grupo.Where(p => p.Posicion == linea.Posicion).Sum(p => p.Cantidad) <
                linea.Cantidad - _partidas.Where(p => p.PedidoFacturableId == pedido.Id && p.Posicion == linea.Posicion && p.ContenedorId != id
                    && Activo(p) && Contenedor(p.ContenedorId).FechaEmbarqueReal is not null).Sum(p => p.Cantidad));
        }
        if (parcial && Contenedor(id).LeerAutorizacion(true)?.VersionEmbarque != VersionEmbarque)
            throw new BusinessRuleException("CE_PARCIAL_SIN_AUTORIZAR", "La parcialidad requiere autorización de Crédito y Cobranza o de su suplente habilitado.");
    }
}
