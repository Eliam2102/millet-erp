using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IComprasOcReadPort"/>
/// declarado en <c>Almacen.Domain.Ports</c>. Reemplaza el
/// <c>NoOpComprasOcReadPort</c> de Almacén (PLATFORM-TODO
/// &lt;ComprasOcReadAdapter&gt;).
///
/// <para>Reside en <c>Compras.Infrastructure.PublicAdapters</c> porque
/// Compras YA referencia Almacén (ProjectReference inverso) y conoce
/// la interfaz; Almacén NO referencia Compras (bounded context limpio).
/// El composition root (<c>Program.cs</c>) cablea esta implementación
/// en lugar del NoOp.</para>
///
/// <para>Lectura cross-módulo via <see cref="ComprasDbContext"/> con
/// <c>AsNoTracking</c>. <see cref="ObtenerAsync"/> (lectura <b>operativa</b>)
/// filtra por estados que aceptan recepción
/// (<see cref="EstadoOrdenCompra.Autorizada"/>) — si la OC no está en
/// estado válido, devuelve <c>null</c> para que el handler de Almacén
/// rechace con <c>RECEPCION_OC_NO_AUTORIZADA</c>. <see cref="ObtenerFoliosAsync"/>
/// (lectura de <b>presentación</b>) es state-agnostic: resuelve el folio
/// aunque la OC ya esté Cerrada/Cancelada (ADR-0042). Dos contratos sobre
/// el mismo puerto; no se mezclan.</para>
///
/// <para><b>Multimoneda</b>: convierte <see cref="OrdenCompra.Moneda"/>
/// + <see cref="OrdenCompra.TipoCambio"/> a MXN. MXN pasa directo;
/// moneda extranjera multiplica por TipoCambio (validado != null por
/// el agregado). Si TipoCambio es null en una OC no-MXN (no debería
/// suceder), el precio se reporta como 0 y el handler de Almacén
/// rechazará en validación de tolerancia.</para>
/// </summary>
public sealed class ComprasOcReadAdapter : IComprasOcReadPort
{
    private readonly ComprasDbContext _db;

    public ComprasOcReadAdapter(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .AsNoTracking()
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == ocId, cancellationToken);

        if (oc is null) return null;

        // Aceptamos solo OCs autorizadas. El estado "Recibida" que
        // menciona la doc del puerto se derivaba de sub-estados antes
        // de F6-PR3; hoy la OC solo necesita estar Autorizada para
        // aceptar recepciones (los sub-estados de recepción/facturación/
        // pago son ortogonales y no bloquean nuevas entradas).
        if (oc.Estado != EstadoOrdenCompra.Autorizada)
        {
            return null;
        }

        var conversionMxn = ConvertirAMxn(oc.Moneda, oc.TipoCambio);

        var lineas = oc.Lineas
            .Select(l => new OcLineaLectura(
                LineaId: l.Id,
                ArticuloId: l.ArticuloId,
                UnidadMedida: l.UnidadMedida,
                CantidadSolicitada: l.Cantidad,
                CantidadRecibida: l.CantidadRecibida,
                PrecioUnitarioMxn: Math.Round(l.PrecioUnitario * conversionMxn, 4)))
            .ToList();

        return new OcLectura(
            Id: oc.Id,
            Folio: oc.Folio.Valor,
            ProveedorId: oc.ProveedorId,
            EmpresaId: oc.EmpresaId,
            Estado: oc.Estado.ToString(),
            Lineas: lineas);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken cancellationToken)
    {
        if (ocIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var distinct = ocIds.Distinct().ToArray();

        // Lectura de presentación: state-agnostic a propósito (NO filtra por
        // estado, a diferencia de ObtenerAsync). El folio debe resolver aunque
        // la OC ya esté Cerrada/Cancelada para mostrarlo en recepciones
        // históricas.
        //
        // Se proyecta el VO Folio (HasConversion ↔ columna string) y se lee
        // .Valor en memoria — acceder a .Valor dentro del árbol SQL no es
        // traducible de forma confiable sobre una propiedad value-converted
        // (lección de #396). Sin Include(Lineas): no materializa el VO Money
        // de las líneas.
        var rows = await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => distinct.Contains(o.Id))
            .Select(o => new { o.Id, o.Folio })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => x.Folio.Valor);
    }

    private static decimal ConvertirAMxn(string moneda, decimal? tipoCambio)
    {
        if (string.Equals(moneda, "MXN", StringComparison.OrdinalIgnoreCase))
        {
            return 1.0m;
        }
        // Si TipoCambio es null en moneda extranjera, devolver 0 fuerza al
        // handler de Almacén a rechazar por tolerancia excedida (cantidad
        // > 0 * cantidad_solicitada = 0). Es el comportamiento defensivo.
        return tipoCambio ?? 0m;
    }
}
