using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Revision;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.Revision;

// ============================================================================
// ALM-04 (G1.3): dictamen sobre una línea de material en revisión. Liberar
// (salida de MAT-REV + reincorporación al rack disponible) y Baja por daño,
// en una sola transacción y auditable en almacen.dictamenes_revision.
// ============================================================================

public sealed record DictaminarMaterialEnRevisionCommand(
    Guid LineaRevisionId, ResultadoDictamen Resultado, decimal Cantidad, string Motivo,
    DateOnly FechaMovimiento, Guid? UbicacionDestinoId) : IRequest<DictamenRevisionResponse>;

public sealed record DictamenRevisionResponse(
    Guid DictamenId, ResultadoDictamen Resultado, string? Folio, Guid? DevolucionAProveedorId);

public sealed class DictaminarMaterialEnRevisionValidator : AbstractValidator<DictaminarMaterialEnRevisionCommand>
{
    public DictaminarMaterialEnRevisionValidator()
    {
        RuleFor(c => c.LineaRevisionId).NotEqual(Guid.Empty);
        RuleFor(c => c.Resultado).IsInEnum();
        RuleFor(c => c.Cantidad).GreaterThan(0);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(500);
        RuleFor(c => c.FechaMovimiento).Must(f => f.Year is >= 2020 and <= 2099);
        RuleFor(c => c.UbicacionDestinoId).NotNull().NotEqual(Guid.Empty)
            .When(c => c.Resultado == ResultadoDictamen.Liberar)
            .WithMessage("Elige la ubicación (rack) disponible a la que se libera el material.");
    }
}

public sealed class DictaminarMaterialEnRevisionHandler(
    AlmacenDbContext db,
    ICurrentUserContext currentUser,
    ICurrentEmpresaContext currentEmpresa,
    IDecimalesUnidadGuard decimalesGuard,
    IPeriodoContableReadPort periodoContable,
    IAdjuntosEntidadReadPort adjuntos,
    IComprasOcReadPort ocPort)
    : IRequestHandler<DictaminarMaterialEnRevisionCommand, DictamenRevisionResponse>
{
    public async Task<DictamenRevisionResponse> Handle(
        DictaminarMaterialEnRevisionCommand request, CancellationToken cancellationToken)
    {
        var info = await new ObtenerLineaRevisionHandler(db)
            .Handle(new ObtenerLineaRevisionQuery(request.LineaRevisionId), cancellationToken)
            ?? throw new EntityNotFoundException("REVISION_LINEA_NO_ENCONTRADA",
                $"No existe una línea de material en revisión con id '{request.LineaRevisionId}'.");

        // R5: dos dictámenes simultáneos sobre la misma línea se serializan por el movimiento de revisión.
        await using var transaccion = await DevolucionesProveedor.DevolucionOrigenLock.AbrirAsync(
            db, info.MovimientoRevisionId, cancellationToken);

        // 86e3tabdp: cancelar la recepción toma este mismo candado; si la cancelaron mientras esperábamos, ya no hay material que dictaminar.
        if (!await db.Movimientos.AsNoTracking().AnyAsync(m => m.Id == info.MovimientoRevisionId && m.Estado == EstadoMovimiento.Registrado, cancellationToken))
            throw new BusinessRuleException("DICTAMEN_RECEPCION_CANCELADA",
                "La recepción de este material fue cancelada: ya no se puede dictaminar.");

        var dictaminado = await db.DictamenesRevision.AsNoTracking()
            .Where(d => d.LineaRevisionId == info.LineaRevisionId
                && (d.Resultado == ResultadoDictamen.Liberar || d.Resultado == ResultadoDictamen.Baja))
            .SumAsync(d => (decimal?)d.Cantidad, cancellationToken) ?? 0m;
        var enDevolucion = await (
            from ld in db.LineasDevolucionProveedor.AsNoTracking()
            join d in db.DevolucionesProveedor.AsNoTracking() on ld.DevolucionId equals d.Id
            where ld.LineaRecepcionOrigenId == info.LineaRevisionId && d.Estado != EstadoDevolucionProveedor.Rechazada
            select (decimal?)ld.Cantidad).SumAsync(cancellationToken) ?? 0m;
        var pendiente = info.Cantidad - dictaminado - enDevolucion;
        if (request.Cantidad > pendiente)
            throw new BusinessRuleException("DICTAMEN_EXCEDE_PENDIENTE",
                $"La cantidad a dictaminar ({request.Cantidad:0.####}) supera lo pendiente en revisión ({pendiente:0.####}).");

        await decimalesGuard.ValidarAsync(
            [Recepciones.RecepcionVariante.Cantidad(info.ArticuloId, request.Cantidad, info.UnidadMedida,
                info.ProductoAwVarianteId, info.AnchoMm, info.AltoMm)], cancellationToken);

        var empresaId = currentEmpresa.Current ?? throw new BusinessRuleException(
            "MOV_SIN_EMPRESA", "El contexto de empresa es requerido.");
        var usuarioId = currentUser.UserId ?? throw new BusinessRuleException(
            "DICTAMEN_SIN_USUARIO", "Se requiere usuario autenticado.");
        await Cierre.PeriodoCerradoValidator.LanzarSiCerradoAsync(
            db, empresaId, request.FechaMovimiento, periodoContable, cancellationToken);

        var costo = await db.SaldosInventario.AsNoTracking()
            .Where(s => s.UbicacionId == info.UbicacionRevisionId && s.ArticuloId == info.ArticuloId && s.ProductoAwVarianteId == info.ProductoAwVarianteId && s.Lote == info.Lote)
            .Select(s => (decimal?)s.CostoPromedioMxn).FirstOrDefaultAsync(cancellationToken)
            ?? info.CostoUnitarioMxn;
        var origen = new LineaRevisionMov(info.ArticuloId, info.UnidadMedida, request.Cantidad, costo, info.UbicacionRevisionId,
            info.ProductoAwVarianteId, info.AnchoMm, info.AltoMm, info.Lote);

        DictamenRevision dictamen;
        string? folio;
        Guid? devolucionId = null;
        switch (request.Resultado)
        {
            case ResultadoDictamen.Liberar:
                var destinoId = request.UbicacionDestinoId!.Value;
                var subDestinoId = await UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(
                    db, destinoId, info.ArticuloId, cancellationToken,
                    esVidrio: info.ProductoAwVarianteId is not null);
                var subDestino = await db.SubAlmacenes.AsNoTracking()
                    .FirstAsync(s => s.Id == subDestinoId, cancellationToken);
                if (subDestino.Tipo == TipoSubAlmacen.MaterialEnRevision)
                    throw new BusinessRuleException("DICTAMEN_DESTINO_EN_REVISION",
                        "La ubicación destino está en el sub-almacén de material en revisión; elige una ubicación disponible.");
                var almacenRevisionId = await db.SubAlmacenes.AsNoTracking()
                    .Where(s => s.Id == info.SubAlmacenRevisionId).Select(s => s.AlmacenId).FirstAsync(cancellationToken);
                if (subDestino.AlmacenId != almacenRevisionId)
                    throw new BusinessRuleException("DICTAMEN_DESTINO_OTRO_ALMACEN",
                        "La ubicación destino debe ser del mismo almacén donde está el material en revisión.");

                var (salida, entrada) = await MovimientosRevision.AgregarParLiberacionAsync(
                    db, empresaId, usuarioId, request.FechaMovimiento, request.Motivo,
                    [new ParLiberacionLinea(origen, destinoId)], cancellationToken);
                dictamen = new DictamenRevision(Guid.CreateVersion7(), empresaId, info.LineaRevisionId,
                    info.MovimientoRevisionId, request.Resultado, request.Cantidad, request.Motivo, usuarioId,
                    movimientoSalidaRevisionId: salida.Id, movimientoEntradaId: entrada.Id);
                folio = entrada.Folio;
                break;

            case ResultadoDictamen.Baja:
                var baja = await MovimientosRevision.AgregarAsync(db, empresaId, usuarioId, TipoMovimiento.BajaPorDano,
                    request.FechaMovimiento, request.Motivo, [origen], cancellationToken);
                dictamen = new DictamenRevision(Guid.CreateVersion7(), empresaId, info.LineaRevisionId,
                    info.MovimientoRevisionId, request.Resultado, request.Cantidad, request.Motivo, usuarioId,
                    movimientoSalidaRevisionId: baja.Id);
                folio = baja.Folio;
                break;

            default: // Rechazar: abre la devolución a proveedor existente (8.B) en EnAutorizacion, con la evidencia.
                var evidencias = await adjuntos.ListarVigentesAsync("linea_revision", info.LineaRevisionId, cancellationToken);
                if (evidencias.Count == 0)
                    throw new BusinessRuleException("DICTAMEN_RECHAZO_SIN_EVIDENCIA",
                        "Adjunta al menos una foto o evidencia del daño antes de rechazar el material.");
                var ordenCompraId = info.OrdenCompraId ?? throw new BusinessRuleException(
                    "DICTAMEN_RECHAZO_SIN_OC", "La línea en revisión no proviene de una orden de compra; no se puede devolver al proveedor.");
                var oc = await ocPort.ObtenerAsync(ordenCompraId, cancellationToken)
                    ?? throw new EntityNotFoundException("DICTAMEN_RECHAZO_OC_NO_ENCONTRADA",
                        $"No existe la orden de compra '{ordenCompraId}' de la recepción.");
                await DevolucionProveedorOrigenGuard.ValidarAsync(db, ocPort, info.MovimientoRevisionId, oc.ProveedorId,
                    [new LineaOrigenDevolucion(info.LineaRevisionId, info.ArticuloId, request.Cantidad)], null, cancellationToken);

                var devolucion = new DevolucionAProveedor(Guid.CreateVersion7(), empresaId, oc.ProveedorId,
                    $"Rechazo por dictamen de calidad: {request.Motivo}", usuarioId,
                    recepcionOrigenId: info.MovimientoRevisionId, ordenCompraOrigenId: ordenCompraId,
                    subAlmacenOrigenId: info.SubAlmacenRevisionId);
                devolucion.AgregarLinea(new LineaDevolucionProveedor(Guid.CreateVersion7(), devolucion.Id, 1,
                    info.ArticuloId, request.Cantidad, info.UnidadMedida, info.CostoUnitarioMxn,
                    lineaRecepcionOrigenId: info.LineaRevisionId));
                foreach (var e in evidencias)
                    devolucion.AgregarEvidencia(new EvidenciaDevolucionProveedor(Guid.CreateVersion7(), devolucion.Id,
                        "evidencia_recepcion", e.NombreArchivo, $"adjunto:{e.Id}", info.MotivoDano));
                devolucion.SolicitarAutorizacion();
                db.Set<DevolucionAProveedor>().Add(devolucion);
                devolucionId = devolucion.Id;
                dictamen = new DictamenRevision(Guid.CreateVersion7(), empresaId, info.LineaRevisionId,
                    info.MovimientoRevisionId, request.Resultado, request.Cantidad, request.Motivo, usuarioId,
                    devolucionAProveedorId: devolucion.Id);
                folio = null;
                break;
        }

        db.DictamenesRevision.Add(dictamen);
        await db.SaveChangesAsync(cancellationToken);
        if (transaccion is not null) await transaccion.CommitAsync(cancellationToken);
        return new DictamenRevisionResponse(dictamen.Id, dictamen.Resultado, folio, devolucionId);
    }
}
