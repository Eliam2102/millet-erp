using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IComprasPedidoVivoReadPort"/> declarado
/// en <c>Almacen.Domain.Ports</c> (ADR-0047 PR5.B). Reemplaza el
/// <c>NoOpComprasPedidoVivoReadPort</c> de Almacén; cableado en <c>Program.cs</c>.
/// Reside en Compras porque Compras YA referencia Almacén (Almacén no referencia
/// Compras); lectura cross-módulo via <see cref="ComprasDbContext"/> con AsNoTracking.
///
/// <para>
/// Calcula "vivo de origen sistema" por (articulo, almacén):
/// <list type="bullet">
///   <item><b>OC:</b> Σ líneas de OC <see cref="EstadoOrdenCompra.Autorizada"/> cuya
///   RQ de origen es Sistema, con <c>Cantidad − CantidadRecibida</c> (por recibir),
///   agrupado por <c>(ArticuloId, AlmacenDestinoId)</c> de la <b>RQ de origen</b>
///   (PR2: la línea de OC ya no guarda almacén; se lee vía join por RequisicionId).</item>
///   <item><b>RQ:</b> Σ líneas de RQ de origen Sistema en estado no terminal, con la
///   porción <b>aún no volcada a una OC viva</b> = <c>max(0, Cantidad − Σ ordenado en
///   líneas de OC vivas que la referencian)</c>, agrupado por <c>(ArticuloId,
///   AlmacenDestinoId de cabecera de la RQ)</c>.</item>
/// </list>
/// El <b>dedup por línea</b> (FK <c>LineaOrdenCompra.LineaRequisicionId</c>) evita el
/// doble conteo y maneja conversión parcial: lo que ya está en OC viva lo cuenta la
/// OC; el resto lo cuenta la RQ. Una línea de RQ volcada a una OC <b>cancelada</b>
/// vuelve a contar por la RQ (esa OC no es viva). Cero contacto con el universo manual.
/// </para>
///
/// <para>
/// <b>Bypass de tenancy acotado:</b> <c>Requisicion</c>/<c>OrdenCompra</c> son
/// <c>IPerteneceAEmpresa</c> → el query filter global (ADR-0011) las recorta a la
/// empresa del contexto, que un motor de fondo (PR5.D) no tiene. Las lecturas se
/// envuelven en un <c>Bypass()</c> <b>acotado</b> (se cierra al terminar la lectura);
/// es seguro porque un <c>almacen_id</c> pertenece a una sola empresa, así que
/// consultar por (articulo, almacén) no mezcla empresas.
/// </para>
/// </summary>
public sealed class ComprasPedidoVivoReadAdapter : IComprasPedidoVivoReadPort
{
    private static readonly EstadoRequisicion[] EstadosVivosRq =
    [
        EstadoRequisicion.Borrador,
        EstadoRequisicion.EnAutorizacion,
        EstadoRequisicion.Autorizada,
        EstadoRequisicion.EnSurtido,
    ];

    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;

    public ComprasPedidoVivoReadAdapter(ComprasDbContext db, ICurrentEmpresaContext empresa)
    {
        _db = db;
        _empresa = empresa;
    }

    private readonly record struct OcRow(
        Guid ArticuloId, Guid AlmacenId, decimal Cantidad, decimal Recibida,
        Guid? RequisicionId, Guid? LineaRqId);

    private readonly record struct RqRow(
        Guid LineaRqId, Guid ArticuloId, Guid AlmacenId, decimal Cantidad);

    public async Task<IReadOnlyDictionary<PedidoVivoClave, decimal>> ObtenerVivoDeSistemaAsync(
        IReadOnlyCollection<PedidoVivoClave> pares,
        CancellationToken cancellationToken)
    {
        var vacio = (IReadOnlyDictionary<PedidoVivoClave, decimal>)new Dictionary<PedidoVivoClave, decimal>();
        if (pares.Count == 0) return vacio;

        var articulos = pares.Select(p => p.ArticuloId).Distinct().ToList();
        var solicitados = new HashSet<PedidoVivoClave>(pares);

        // ── Lecturas empresa-filtradas: bypass ACOTADO (se cierra al salir del using) ──
        HashSet<Guid> systemRqIds;
        using (_empresa.Bypass())
        {
            systemRqIds = (await _db.Requisiciones.AsNoTracking()
                .Where(r => r.Origen == OrigenRequisicion.Sistema)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        }
        if (systemRqIds.Count == 0) return vacio;

        List<OcRow> ocRows;
        List<RqRow> rqRows;
        using (_empresa.Bypass())
        {
            // Almacén-por-línea PR2: la línea de OC ya no guarda almacén; el
            // almacén del reorden vive en la RQ de origen (sistema), leído vía
            // join por RequisicionId.
            //
            // PR3 (blocker fix): la RQ manual ahora tiene AlmacenDestinoId = NULL.
            // El filtro `rqOrigen.Origen == Sistema` en el WHERE excluye las
            // líneas de OC de origen MANUAL *antes* de proyectar — evita
            // materializar NULL en el `Guid` no-nullable de OcRow.AlmacenId
            // (crash) y hace redundante el filtro in-memory `systemRqIds`. Solo
            // llegan RQs de sistema, con almacén siempre poblado → `.Value` seguro.
            ocRows = await (
                from ol in _db.LineasOrdenCompra.AsNoTracking()
                join oc in _db.OrdenesCompra.AsNoTracking() on ol.OrdenCompraId equals oc.Id
                join rqOrigen in _db.Requisiciones.AsNoTracking() on ol.RequisicionId equals rqOrigen.Id
                where oc.Estado == EstadoOrdenCompra.Autorizada
                   && ol.RequisicionId != null
                   && rqOrigen.Origen == OrigenRequisicion.Sistema
                   && articulos.Contains(ol.ArticuloId)
                select new OcRow(
                    ol.ArticuloId, rqOrigen.AlmacenDestinoId!.Value, ol.Cantidad, ol.CantidadRecibida,
                    ol.RequisicionId, ol.LineaRequisicionId))
                .ToListAsync(cancellationToken);

            rqRows = await (
                from rl in _db.LineaRequisiciones.AsNoTracking()
                join rq in _db.Requisiciones.AsNoTracking() on rl.RequisicionId equals rq.Id
                where rq.Origen == OrigenRequisicion.Sistema
                   && EstadosVivosRq.Contains(rq.Estado)
                   && articulos.Contains(rl.ArticuloId)
                // PR3: filtra Origen==Sistema → AlmacenDestinoId siempre poblado, .Value seguro.
                select new RqRow(rl.Id, rl.ArticuloId, rq.AlmacenDestinoId!.Value, rl.Cantidad))
                .ToListAsync(cancellationToken);
        }
        // ── A partir de aquí, cómputo en memoria (sin DB, sin bypass) ──

        // OC pendiente por clave + ordenado por línea-RQ (para el dedup). Todas
        // las filas de ocRows ya son de RQ de origen Sistema (filtrado en SQL,
        // PR3) — el filtro in-memory por systemRqIds era redundante y se retiró.
        var ocPendientePorClave = new Dictionary<PedidoVivoClave, decimal>();
        var ocOrdenadoPorLineaRq = new Dictionary<Guid, decimal>();
        foreach (var ol in ocRows)
        {
            var pendiente = ol.Cantidad - ol.Recibida;
            if (pendiente > 0m)
            {
                var clave = new PedidoVivoClave(ol.ArticuloId, ol.AlmacenId);
                ocPendientePorClave[clave] = ocPendientePorClave.GetValueOrDefault(clave) + pendiente;
            }
            if (ol.LineaRqId is Guid lrq)
                ocOrdenadoPorLineaRq[lrq] = ocOrdenadoPorLineaRq.GetValueOrDefault(lrq) + ol.Cantidad;
        }

        // RQ: porción aún no ordenada en OC viva (dedup por línea, maneja parcial).
        var rqPorClave = new Dictionary<PedidoVivoClave, decimal>();
        foreach (var rl in rqRows)
        {
            var yaOrdenado = ocOrdenadoPorLineaRq.GetValueOrDefault(rl.LineaRqId);
            var sinOc = rl.Cantidad - yaOrdenado;
            if (sinOc > 0m)
            {
                var clave = new PedidoVivoClave(rl.ArticuloId, rl.AlmacenId);
                rqPorClave[clave] = rqPorClave.GetValueOrDefault(clave) + sinOc;
            }
        }

        // Suma OC + RQ, solo para los pares solicitados y con total > 0.
        var resultado = new Dictionary<PedidoVivoClave, decimal>();
        foreach (var clave in ocPendientePorClave.Keys.Union(rqPorClave.Keys))
        {
            if (!solicitados.Contains(clave)) continue;
            var total = ocPendientePorClave.GetValueOrDefault(clave) + rqPorClave.GetValueOrDefault(clave);
            if (total > 0m) resultado[clave] = total;
        }
        return resultado;
    }
}
