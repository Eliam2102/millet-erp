using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.AlmCompra;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Integration.AlmCompra;

/// <summary>
/// Arma los mensajes ERP → Almacenes y los deja en la bandeja (<see cref="AlmCompraEnvio"/>) SIN guardar: el llamador
/// los guarda en la misma transacción que el cambio de la OC. Solo cuentan las líneas de la OC que surten una RQ de la
/// app de Almacenes; una OC sin ninguna no genera nada. Un fallo de armado no bloquea la autorización: deja la fila
/// en ERROR, visible y reintentable.
/// </summary>
public sealed class AlmCompraEncolador(ComprasDbContext db, CompartidoDbContext maestros, IClock clock)
{
    /// <summary>OC autorizada: COMPRA_RECIBIDA; si ya se mandó una (reautorización tras reabrir), COMPRA_MODIFICADA si cambió algo.</summary>
    public async Task OcAutorizadaAsync(Guid ocId, CancellationToken ct)
    {
        var (oc, previos) = await CargarAsync(ocId, ct);
        if (oc is null || previos.Any(p => p.Tipo == AlmCompraEnvio.TipoCancelada)) return;
        var recibida = previos.Any(p => p.Tipo == AlmCompraEnvio.TipoRecibida);
        await EncolarCompraAsync(oc, previos, recibida ? AlmCompraEnvio.TipoModificada : AlmCompraEnvio.TipoRecibida, ct);
    }

    /// <summary>Pedimento (o BL, ETA, agente) editado en una OC autorizada: COMPRA_MODIFICADA completa, solo si ya se mandó la compra y hay cambio.</summary>
    public async Task PedimentoActualizadoAsync(Guid ocId, CancellationToken ct)
    {
        var (oc, previos) = await CargarAsync(ocId, ct);
        if (oc is null || previos.Any(p => p.Tipo == AlmCompraEnvio.TipoCancelada)
            || !previos.Any(p => p.Tipo == AlmCompraEnvio.TipoRecibida)) return;
        await EncolarCompraAsync(oc, previos, AlmCompraEnvio.TipoModificada, ct);
    }

    /// <summary>
    /// OC cancelada: COMPRA_CANCELADA (obligatoria si la compra ya se mandó). Con material ya recibido se deja en ERROR local:
    /// Almacenes solo acepta cancelar mientras no haya entrada cerrada (supuesto: «recibido» es el indicio que tiene el ERP).
    /// </summary>
    public async Task OcCanceladaAsync(Guid ocId, string? motivo, CancellationToken ct)
    {
        var (oc, previos) = await CargarAsync(ocId, ct);
        if (oc is null || !previos.Any(p => p.Tipo == AlmCompraEnvio.TipoRecibida)
            || previos.Any(p => p.Tipo == AlmCompraEnvio.TipoCancelada)) return;
        var (ordinal, mensajeId) = Siguiente(oc, previos, AlmCompraEnvio.TipoCancelada);
        if (oc.Lineas.Sum(l => l.CantidadRecibida) > 0)
        {
            db.AlmCompraEnvios.Add(AlmCompraEnvio.ConErrorLocal(Guid.CreateVersion7(), oc.EmpresaId, oc.Id, oc.Folio.Valor,
                AlmCompraEnvio.TipoCancelada, ordinal, mensajeId,
                "COMPRA_CON_ENTRADA: la OC ya tiene material recibido; Almacenes solo acepta cancelar sin entrada cerrada."));
            return;
        }
        db.AlmCompraEnvios.Add(Nuevo(oc, AlmCompraEnvio.TipoCancelada, ordinal, mensajeId,
            AlmCompraSobre.DatosCancelada(oc.Folio.Valor, motivo)));
    }

    /// <summary>Rearma el sobre de una fila que falló al armarse (para «reintentar»). Devuelve <c>null</c> si la OC ya no genera compra.</summary>
    public async Task<string?> ReconstruirSobreAsync(AlmCompraEnvio e, CancellationToken ct)
    {
        var oc = await db.OrdenesCompra.Include(o => o.Lineas).FirstOrDefaultAsync(o => o.Id == e.OrdenCompraId, ct)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "La OC del mensaje ya no existe.");
        if (e.Tipo == AlmCompraEnvio.TipoCancelada)
            return AlmCompraSobre.Armar(e.MensajeId, e.Tipo, clock.UtcNow, e.Referencia, AlmCompraSobre.DatosCancelada(oc.Folio.Valor, null));
        var datos = await ConstruirDatosAsync(oc, ct);
        return datos is null ? null
            : AlmCompraSobre.Armar(e.MensajeId, e.Tipo, clock.UtcNow, e.Referencia, AlmCompraSobre.DatosCompra(datos));
    }

    private async Task<(OrdenCompra? Oc, List<AlmCompraEnvio> Previos)> CargarAsync(Guid ocId, CancellationToken ct)
    {
        var oc = await db.OrdenesCompra.Include(o => o.Lineas).FirstOrDefaultAsync(o => o.Id == ocId, ct);
        if (oc is null) return (null, []);
        var previos = await db.AlmCompraEnvios.AsNoTracking().Where(e => e.OrdenCompraId == ocId).OrderBy(e => e.Secuencia).ToListAsync(ct);
        return (oc, previos);
    }

    private async Task EncolarCompraAsync(OrdenCompra oc, List<AlmCompraEnvio> previos, string tipo, CancellationToken ct)
    {
        var (ordinal, mensajeId) = Siguiente(oc, previos, tipo);
        try
        {
            var datos = await ConstruirDatosAsync(oc, ct);
            if (datos is null) return; // la OC no surte ninguna RQ de la app
            if (tipo == AlmCompraEnvio.TipoModificada)
            {
                var ultimo = previos.LastOrDefault(p => p.Tipo != AlmCompraEnvio.TipoCancelada && p.Sobre.Length > 0);
                if (ultimo is not null && AlmCompraSobre.TraeLosMismosDatos(ultimo.Sobre, datos)) return;
            }
            db.AlmCompraEnvios.Add(Nuevo(oc, tipo, ordinal, mensajeId, AlmCompraSobre.DatosCompra(datos)));
        }
        catch (BusinessRuleException ex)
        {
            db.AlmCompraEnvios.Add(AlmCompraEnvio.ConErrorLocal(Guid.CreateVersion7(), oc.EmpresaId, oc.Id, oc.Folio.Valor,
                tipo, ordinal, mensajeId, $"{ex.Code}: {ex.Message}"));
        }
    }

    private static (int Ordinal, string MensajeId) Siguiente(OrdenCompra oc, List<AlmCompraEnvio> previos, string tipo)
    {
        var ordinal = previos.Count(p => p.Tipo == tipo) + 1;
        return (ordinal, AlmCompraSobre.MensajeId(oc.Folio.Valor, oc.Id, tipo, ordinal));
    }

    private AlmCompraEnvio Nuevo(OrdenCompra oc, string tipo, int ordinal, string mensajeId, object datos) =>
        new(Guid.CreateVersion7(), oc.EmpresaId, oc.Id, oc.Folio.Valor, tipo, ordinal, mensajeId,
            AlmCompraSobre.Armar(mensajeId, tipo, clock.UtcNow, oc.Folio.Valor, datos));

    /// <summary>Compra completa tal como está hoy la OC; <c>null</c> si ninguna línea surte una RQ de la app.</summary>
    internal async Task<AlmCompraDatos?> ConstruirDatosAsync(OrdenCompra oc, CancellationToken ct)
    {
        var conRq = oc.Lineas.Where(l => l.LineaRequisicionId is not null).ToList();
        if (conRq.Count == 0) return null;
        var lineaRqIds = conRq.Select(l => l.LineaRequisicionId!.Value).ToList();
        var rqs = await (from l in db.LineaRequisiciones.AsNoTracking()
                         join r in db.Requisiciones.AsNoTracking() on l.RequisicionId equals r.Id
                         where lineaRqIds.Contains(l.Id) && r.Origen == OrigenRequisicion.AlmacenesApp
                         select new { l.Id, l.Posicion, l.AnchoMm, l.AltoMm, r.FolioExterno }).ToDictionaryAsync(x => x.Id, ct);
        if (rqs.Count == 0) return null;

        var proveedor = await maestros.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == oc.ProveedorId, ct)
            ?? throw new BusinessRuleException("ALM_COMPRA_PROVEEDOR_NO_ENCONTRADO", "El proveedor de la OC no existe.");
        if (proveedor.ClaveAw is not int claveAw)
            throw new BusinessRuleException("ALM_COMPRA_PROVEEDOR_SIN_CLAVE_AW", "El proveedor de la OC no tiene clave A+W; Almacenes la exige igual que la de la requisición.");
        if (oc.Moneda != "MXN" && oc.TipoCambio is not > 0)
            throw new BusinessRuleException("ALM_COMPRA_SIN_TIPO_CAMBIO", "La OC no es en MXN y no tiene tipo de cambio.");

        var articuloIds = conRq.Select(l => l.ArticuloId).Distinct().ToList();
        var articulosAw = await (from a in maestros.Articulos.AsNoTracking()
                                 join p in maestros.ProductosAw.AsNoTracking() on a.ProductoAwId equals p.Id
                                 where articuloIds.Contains(a.Id)
                                 select new { a.Id, p.ReferenciaExterna }).ToDictionaryAsync(x => x.Id, x => x.ReferenciaExterna, ct);
        var lineas = new List<AlmCompraLinea>();
        foreach (var l in conRq.OrderBy(x => x.Posicion))
        {
            if (!rqs.TryGetValue(l.LineaRequisicionId!.Value, out var rq)) continue; // línea de una RQ que no es de la app
            if (!articulosAw.TryGetValue(l.ArticuloId, out var refAw) || !int.TryParse(refAw, out var articuloAw))
                throw new BusinessRuleException("ALM_COMPRA_ARTICULO_AW_INVALIDO", $"La línea {l.Posicion} no tiene un artículo A+W numérico.");
            var ancho = l.AnchoMm ?? rq.AnchoMm;
            var alto = l.AltoMm ?? rq.AltoMm;
            var (piezas, recibidas, costoM2, costoPieza) = AlmCompraSobre.Calcular(
                l.UnidadMedida, l.Cantidad, l.CantidadRecibida, l.SubtotalLinea, ancho, alto);
            if (piezas < recibidas)
                throw new BusinessRuleException("ALM_COMPRA_MODIFICADA_BAJO_RECIBIDO", $"La línea {l.Posicion} quedaría con menos piezas ({piezas}) que las ya recibidas ({recibidas}).");
            var medida = ancho > 0 && alto > 0;
            // Jorge 30-oct: artículo A+W + medida; no amarrarse a articuloAlmacenAw (no se manda).
            lineas.Add(new AlmCompraLinea(
                l.Posicion.ToString(), rq.FolioExterno!, rq.Posicion, articuloAw, null,
                medida ? (int)ancho! : null, medida ? (int)alto! : null, piezas,
                l.LoteProveedor, AlmCompraSobre.FormatoPedimento(oc.InfoImportNumeroPedimento), null, costoM2, costoPieza));
        }
        if (lineas.Count == 0) return null;
        return new AlmCompraDatos(oc.Folio.Valor, claveAw, proveedor.RazonSocial, oc.Moneda, oc.TipoCambio,
            oc.FechaDocumento, lineas);
    }
}
