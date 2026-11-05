using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Salidas;

// Carga pieza por pieza (PT5.4). Mismo permiso y alcance por sucursal que el resto de la hoja de salida (excepciones.gestionar).
internal static class CargaPieza
{
    /// <summary>Hoja editable (Borrador), pieza por etiqueta y su albarán. Rechaza lo que no es de la ruta.</summary>
    public static async Task<(SalidaPt Hoja, PiezaPt Pieza, PedidoPt Pedido, AlbaranPt Albaran)> ResolverAsync(ProductoTerminadoDbContext db, PtAcceso acceso, Guid hojaId, string etiqueta, CancellationToken ct)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, ct);
        var hoja = await SalidaMapeo.Completa(db).FirstOrDefaultAsync(x => x.Id == hojaId, ct)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(hoja.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, ct);
        if (hoja.Estado != EstadoSalida.Borrador) throw new BusinessRuleException("PT_SALIDA_NO_EDITABLE", "La hoja de salida ya no está en carga.");

        etiqueta = etiqueta.Trim();
        var pieza = await db.Piezas.FirstOrDefaultAsync(p => p.Etiqueta == etiqueta, ct)
            ?? throw new BusinessRuleException("PT_CARGA_PIEZA_NO_EXISTE", $"La etiqueta {etiqueta} no existe en Producto Terminado.");
        if (pieza.SucursalId != hoja.SucursalId)
            throw new BusinessRuleException("PT_SALIDA_PIEZA_OTRA_SUCURSAL", $"La pieza {etiqueta} está en otra sucursal.");
        var pedido = await db.PedidosPt.FirstAsync(p => p.Id == pieza.PedidoPtId, ct);
        var albaran = hoja.Albaranes.FirstOrDefault(a => a.PedidoPtId == pieza.PedidoPtId)
            ?? throw new BusinessRuleException("PT_CARGA_FUERA_DE_RUTA", $"La pieza {etiqueta} es del pedido {pedido.NumeroPedido}, que no está en esta ruta.");
        return (hoja, pieza, pedido, albaran);
    }

    public static async Task<HojaSalidaDto> DevolverAsync(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidos, ICurrentEmpresaContext empresa, Guid hojaId, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        var s = await SalidaMapeo.Completa(db).AsNoTracking().FirstAsync(x => x.Id == hojaId, ct);
        return await SalidaMapeo.ADtoAsync(db, pedidos, empresa.Current ?? throw new UnauthorizedAccessException(), s, true, false, ct);
    }
}

public sealed class CargarPiezaHojaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<CargarPiezaHojaCommand, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(CargarPiezaHojaCommand c, CancellationToken cancellationToken)
    {
        var (hoja, pieza, pedido, albaran) = await CargaPieza.ResolverAsync(db, acceso, c.HojaId, c.Etiqueta, cancellationToken);
        // Caja: el pedido con saldo pendiente no se carga sin liberación (PT5.4-a); se revisa en cada pieza porque la liberación puede cambiar mientras se carga.
        if (pedido.Liberacion != EstadoLiberacion.Liberado)
            throw new BusinessRuleException("PT_PEDIDO_NO_LIBERADO", $"El pedido {pedido.NumeroPedido} tiene saldo pendiente en caja: no se carga sin liberación.");
        if (albaran.Piezas.Any(p => p.PiezaId == pieza.Id))
            throw new BusinessRuleException("PT_CARGA_YA_CARGADA", $"La pieza {pieza.Etiqueta} ya está cargada en esta hoja.");
        var enOtraHoja = await (from ap in db.AlbaranPiezas join a in db.Albaranes on ap.AlbaranPtId equals a.Id join s in db.Salidas on a.SalidaPtId equals s.Id
                                where ap.PiezaId == pieza.Id && s.Estado != EstadoSalida.Cancelada select ap.PiezaId).AnyAsync(cancellationToken);
        if (enOtraHoja || pieza.Estado != EstadoPieza.EnPT)
            throw new BusinessRuleException("PT_SALIDA_PIEZA_YA_EN_HOJA", $"La pieza {pieza.Etiqueta} ya está en otra hoja de salida o ya salió.");
        if ((await SalidaMapeo.RotasAsync(db, [pieza.Id], cancellationToken)).Count > 0)
            throw new BusinessRuleException("PT_CARGA_PIEZA_ROTA", $"La pieza {pieza.Etiqueta} se marcó como rota y espera su reposición.");

        var ahora = clock.UtcNow;
        pieza.Cargar();
        db.AlbaranPiezas.Add(new AlbaranPtPieza(hoja.EmpresaId, albaran.Id, pieza.Id));
        db.Movimientos.Add(MovimientoPieza.PasoPieza(pieza, TipoMovimientoPieza.Carga, EstadoPieza.EnPT, albaran.Id, ahora, acceso.Actor.ToString()));
        return await CargaPieza.DevolverAsync(db, pedidosPort, empresa, hoja.Id, cancellationToken);
    }
}

public sealed class DescargarPiezaHojaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<DescargarPiezaHojaCommand, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(DescargarPiezaHojaCommand c, CancellationToken cancellationToken)
    {
        var (hoja, pieza, _, albaran) = await CargaPieza.ResolverAsync(db, acceso, c.HojaId, c.Etiqueta, cancellationToken);
        var fila = albaran.Piezas.FirstOrDefault(p => p.PiezaId == pieza.Id)
            ?? throw new BusinessRuleException("PT_CARGA_NO_CARGADA", $"La pieza {pieza.Etiqueta} no está cargada en esta hoja.");
        pieza.RevertirCarga();
        db.AlbaranPiezas.Remove(fila);
        db.Movimientos.Add(MovimientoPieza.PasoPieza(pieza, TipoMovimientoPieza.CargaRevertida, EstadoPieza.Cargada, albaran.Id, clock.UtcNow, acceso.Actor.ToString()));
        return await CargaPieza.DevolverAsync(db, pedidosPort, empresa, hoja.Id, cancellationToken);
    }
}

public sealed class MarcarPiezaRotaHojaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<MarcarPiezaRotaHojaCommand, HojaSalidaDto>
{
    // ponytail: la rota sigue contando en PT hasta la baja por rotura (PT5.8); aquí solo queda pendiente de reposición y fuera del albarán.
    public async Task<HojaSalidaDto> Handle(MarcarPiezaRotaHojaCommand c, CancellationToken cancellationToken)
    {
        var (hoja, pieza, _, albaran) = await CargaPieza.ResolverAsync(db, acceso, c.HojaId, c.Etiqueta, cancellationToken);
        var ahora = clock.UtcNow;
        var fila = albaran.Piezas.FirstOrDefault(p => p.PiezaId == pieza.Id);
        if (fila is not null)
        {
            pieza.RevertirCarga();
            db.AlbaranPiezas.Remove(fila);
            db.Movimientos.Add(MovimientoPieza.PasoPieza(pieza, TipoMovimientoPieza.CargaRevertida, EstadoPieza.Cargada, albaran.Id, ahora, acceso.Actor.ToString()));
        }
        else if (pieza.Estado != EstadoPieza.EnPT)
            throw new BusinessRuleException("PT_PIEZA_NO_EN_PT", $"La pieza {pieza.Etiqueta} no está en PT: ya salió, está en otra hoja o en tránsito.");
        if ((await SalidaMapeo.RotasAsync(db, [pieza.Id], cancellationToken)).Count == 0)
            db.Movimientos.Add(MovimientoPieza.PasoPieza(pieza, TipoMovimientoPieza.RotaEnCarga, EstadoPieza.EnPT, albaran.Id, ahora.AddTicks(1) /* después de la descarga del mismo instante */, acceso.Actor.ToString()));
        return await CargaPieza.DevolverAsync(db, pedidosPort, empresa, hoja.Id, cancellationToken);
    }
}
