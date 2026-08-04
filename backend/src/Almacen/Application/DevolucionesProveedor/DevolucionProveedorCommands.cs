using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.DevolucionesProveedor;

// ============================================================================
// Comandos del sub-flujo 8.B — Devolución a Proveedor (F6-PR1).
// Ciclo completo: Iniciar → AdjuntarEvidencia → SolicitarAutorizacion
// → Autorizar → RegistrarSalida (publica OcDevolucionRegistradaEvent).
// El listener NotaCreditoProveedorRegistradaHandler cierra el ciclo
// marcando ConciliadaConNcFiscal cuando llega TipoRelacionCfdi=3.
// ============================================================================

// ─── Iniciar ─────────────────────────────────────────────────────────────────

public sealed record IniciarDevolucionAProveedorCommand(
    Guid ProveedorId,
    string Motivo,
    Guid? RecepcionOrigenId,
    Guid? FacturaProveedorOrigenId,
    Guid? OrdenCompraOrigenId,
    Guid? SubAlmacenOrigenId,
    IReadOnlyList<DevolucionProveedorLineaInput> Lineas) : IRequest<IniciarDevolucionAProveedorResponse>;

public sealed record DevolucionProveedorLineaInput(
    Guid ArticuloId,
    decimal Cantidad,
    string UnidadMedida,
    decimal CostoUnitarioMxn,
    Guid? LineaRecepcionOrigenId);

public sealed record IniciarDevolucionAProveedorResponse(Guid DevolucionId);

public sealed class IniciarDevolucionAProveedorValidator
    : AbstractValidator<IniciarDevolucionAProveedorCommand>
{
    public IniciarDevolucionAProveedorValidator()
    {
        RuleFor(c => c.ProveedorId).NotEqual(Guid.Empty);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(1000);
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.ArticuloId).NotEqual(Guid.Empty);
            l.RuleFor(x => x.Cantidad).GreaterThan(0);
            l.RuleFor(x => x.UnidadMedida).NotEmpty().MaximumLength(20);
            l.RuleFor(x => x.CostoUnitarioMxn).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class IniciarDevolucionAProveedorHandler
    : IRequestHandler<IniciarDevolucionAProveedorCommand, IniciarDevolucionAProveedorResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public IniciarDevolucionAProveedorHandler(
        AlmacenDbContext db, ICurrentUserContext currentUser, ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db; _currentUser = currentUser; _currentEmpresa = currentEmpresa;
        _decimalesGuard = decimalesGuard;
    }

    public async Task<IniciarDevolucionAProveedorResponse> Handle(
        IniciarDevolucionAProveedorCommand request, CancellationToken cancellationToken)
    {
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "DEV_PROV_SIN_EMPRESA", "El contexto de empresa es requerido.");
        var solicitadaPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "DEV_PROV_SIN_USUARIO", "Se requiere usuario autenticado.");

        // ADR-0046 Etapa 2: valida los decimales de cada línea contra la unidad
        // del artículo (FK NULL → no valida). Batch, un solo round-trip.
        await _decimalesGuard.ValidarAsync(
            request.Lineas.Select(l => new CantidadAValidar(l.ArticuloId, l.Cantidad, l.UnidadMedida)),
            cancellationToken);

        var id = Guid.CreateVersion7();
        var devolucion = new DevolucionAProveedor(
            id: id,
            empresaId: empresaId,
            proveedorId: request.ProveedorId,
            motivo: request.Motivo,
            solicitadaPor: solicitadaPor,
            recepcionOrigenId: request.RecepcionOrigenId,
            facturaProveedorOrigenId: request.FacturaProveedorOrigenId,
            ordenCompraOrigenId: request.OrdenCompraOrigenId,
            subAlmacenOrigenId: request.SubAlmacenOrigenId);

        var posicion = 1;
        foreach (var input in request.Lineas)
        {
            devolucion.AgregarLinea(new LineaDevolucionProveedor(
                id: Guid.CreateVersion7(),
                devolucionId: id,
                posicion: posicion++,
                articuloId: input.ArticuloId,
                cantidad: input.Cantidad,
                unidadMedida: input.UnidadMedida,
                costoUnitarioMxn: input.CostoUnitarioMxn,
                lineaRecepcionOrigenId: input.LineaRecepcionOrigenId));
        }

        _db.Set<DevolucionAProveedor>().Add(devolucion);
        await _db.SaveChangesAsync(cancellationToken);
        return new IniciarDevolucionAProveedorResponse(id);
    }
}

// ─── Adjuntar evidencia ──────────────────────────────────────────────────────

public sealed record AdjuntarEvidenciaDevolucionAProveedorCommand(
    Guid DevolucionId,
    string TipoEvidencia,
    string NombreArchivo,
    string BlobRef,
    string? Comentario) : IRequest;

public sealed class AdjuntarEvidenciaDevolucionAProveedorValidator
    : AbstractValidator<AdjuntarEvidenciaDevolucionAProveedorCommand>
{
    public AdjuntarEvidenciaDevolucionAProveedorValidator()
    {
        RuleFor(c => c.DevolucionId).NotEqual(Guid.Empty);
        RuleFor(c => c.TipoEvidencia).NotEmpty().MaximumLength(50);
        RuleFor(c => c.NombreArchivo).NotEmpty().MaximumLength(254);
        RuleFor(c => c.BlobRef).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Comentario!).MaximumLength(500).When(c => c.Comentario is not null);
    }
}

public sealed class AdjuntarEvidenciaDevolucionAProveedorHandler
    : IRequestHandler<AdjuntarEvidenciaDevolucionAProveedorCommand>
{
    private readonly AlmacenDbContext _db;
    public AdjuntarEvidenciaDevolucionAProveedorHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(
        AdjuntarEvidenciaDevolucionAProveedorCommand request, CancellationToken cancellationToken)
    {
        var dev = await _db.Set<DevolucionAProveedor>()
            .Include(d => d.Evidencias)
            .FirstOrDefaultAsync(d => d.Id == request.DevolucionId, cancellationToken)
            ?? throw new EntityNotFoundException("DEV_PROV_NO_ENCONTRADA",
                $"No existe devolución con id '{request.DevolucionId}'.");

        dev.AgregarEvidencia(new EvidenciaDevolucionProveedor(
            id: Guid.CreateVersion7(),
            devolucionId: request.DevolucionId,
            tipoEvidencia: request.TipoEvidencia,
            nombreArchivo: request.NombreArchivo,
            blobRef: request.BlobRef,
            comentario: request.Comentario));

        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Solicitar autorización ──────────────────────────────────────────────────

public sealed record SolicitarAutorizacionDevolucionAProveedorCommand(Guid DevolucionId) : IRequest;

public sealed class SolicitarAutorizacionDevolucionAProveedorHandler
    : IRequestHandler<SolicitarAutorizacionDevolucionAProveedorCommand>
{
    private readonly AlmacenDbContext _db;
    public SolicitarAutorizacionDevolucionAProveedorHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(
        SolicitarAutorizacionDevolucionAProveedorCommand request, CancellationToken cancellationToken)
    {
        var dev = await _db.Set<DevolucionAProveedor>()
            .Include(d => d.Lineas)
            .FirstOrDefaultAsync(d => d.Id == request.DevolucionId, cancellationToken)
            ?? throw new EntityNotFoundException("DEV_PROV_NO_ENCONTRADA",
                $"No existe devolución con id '{request.DevolucionId}'.");

        dev.SolicitarAutorizacion();
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Autorizar (Dirección) ───────────────────────────────────────────────────

public sealed record AutorizarDevolucionAProveedorCommand(Guid DevolucionId) : IRequest;

public sealed class AutorizarDevolucionAProveedorHandler
    : IRequestHandler<AutorizarDevolucionAProveedorCommand>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public AutorizarDevolucionAProveedorHandler(AlmacenDbContext db, ICurrentUserContext currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task Handle(
        AutorizarDevolucionAProveedorCommand request, CancellationToken cancellationToken)
    {
        var dev = await _db.Set<DevolucionAProveedor>()
            .Include(d => d.Evidencias)
            .FirstOrDefaultAsync(d => d.Id == request.DevolucionId, cancellationToken)
            ?? throw new EntityNotFoundException("DEV_PROV_NO_ENCONTRADA",
                $"No existe devolución con id '{request.DevolucionId}'.");

        var autorizadoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "DEV_PROV_SIN_USUARIO", "Se requiere usuario autenticado.");
        dev.Autorizar(autorizadoPor);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Rechazar ────────────────────────────────────────────────────────────────

public sealed record RechazarDevolucionAProveedorCommand(
    Guid DevolucionId,
    string MotivoRechazo) : IRequest;

public sealed class RechazarDevolucionAProveedorValidator
    : AbstractValidator<RechazarDevolucionAProveedorCommand>
{
    public RechazarDevolucionAProveedorValidator()
    {
        RuleFor(c => c.DevolucionId).NotEqual(Guid.Empty);
        RuleFor(c => c.MotivoRechazo).NotEmpty().MaximumLength(500);
    }
}

public sealed class RechazarDevolucionAProveedorHandler
    : IRequestHandler<RechazarDevolucionAProveedorCommand>
{
    private readonly AlmacenDbContext _db;
    public RechazarDevolucionAProveedorHandler(AlmacenDbContext db) => _db = db;

    public async Task Handle(
        RechazarDevolucionAProveedorCommand request, CancellationToken cancellationToken)
    {
        var dev = await _db.Set<DevolucionAProveedor>()
            .FirstOrDefaultAsync(d => d.Id == request.DevolucionId, cancellationToken)
            ?? throw new EntityNotFoundException("DEV_PROV_NO_ENCONTRADA",
                $"No existe devolución con id '{request.DevolucionId}'.");

        dev.Rechazar(request.MotivoRechazo);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Registrar salida física (publica OcDevolucionRegistradaEvent) ───────────

public sealed record RegistrarSalidaDevolucionAProveedorCommand(
    Guid DevolucionId,
    Guid SubAlmacenId,
    DateOnly FechaMovimiento,
    // C7.2b: bin real por línea de devolución, capturado al REGISTRAR (no al
    // iniciar/autorizar). Correlaciona por LineaDevolucionId. Opcional — sin
    // bin, la línea cae a la ÚNICA (fallback del trigger); salida, sin
    // exigir asignación.
    IReadOnlyList<DevolucionProveedorSalidaLineaBin>? Bins = null)
    : IRequest<RegistrarSalidaDevolucionAProveedorResponse>;

public sealed record DevolucionProveedorSalidaLineaBin(
    Guid LineaDevolucionId,
    Guid UbicacionId);

public sealed record RegistrarSalidaDevolucionAProveedorResponse(
    Guid DevolucionId,
    Guid MovimientoSalidaId,
    string FolioSalida);

public sealed class RegistrarSalidaDevolucionAProveedorValidator
    : AbstractValidator<RegistrarSalidaDevolucionAProveedorCommand>
{
    public RegistrarSalidaDevolucionAProveedorValidator()
    {
        RuleFor(c => c.DevolucionId).NotEqual(Guid.Empty);
        RuleFor(c => c.SubAlmacenId).NotEqual(Guid.Empty);
        RuleFor(c => c.FechaMovimiento)
            .Must(f => f.Year is >= 2020 and <= 2099);
    }
}

public sealed class RegistrarSalidaDevolucionAProveedorHandler
    : IRequestHandler<RegistrarSalidaDevolucionAProveedorCommand,
        RegistrarSalidaDevolucionAProveedorResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IComprasOcReadPort _ocPort;

    public RegistrarSalidaDevolucionAProveedorHandler(
        AlmacenDbContext db,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IComprasOcReadPort ocPort)
    {
        _db = db; _events = events; _currentUser = currentUser; _currentEmpresa = currentEmpresa;
        _ocPort = ocPort;
    }

    public async Task<RegistrarSalidaDevolucionAProveedorResponse> Handle(
        RegistrarSalidaDevolucionAProveedorCommand request, CancellationToken cancellationToken)
    {
        var dev = await _db.Set<DevolucionAProveedor>()
            .Include(d => d.Lineas)
            .FirstOrDefaultAsync(d => d.Id == request.DevolucionId, cancellationToken)
            ?? throw new EntityNotFoundException("DEV_PROV_NO_ENCONTRADA",
                $"No existe devolución con id '{request.DevolucionId}'.");

        // Validar sub-almacén origen físico.
        _ = await _db.SubAlmacenes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SubAlmacenId, cancellationToken)
            ?? throw new EntityNotFoundException("SUBALMACEN_NO_ENCONTRADO",
                $"No existe sub-almacén con id '{request.SubAlmacenId}'.");

        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "DEV_PROV_SIN_EMPRESA", "El contexto de empresa es requerido.");
        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "DEV_PROV_SIN_USUARIO", "Se requiere usuario autenticado.");

        // Crear movimiento físico tipo SalidaPorDevolucionAProveedor.
        var movId = Guid.CreateVersion7();
        var movimiento = new MovimientoInventario(
            id: movId,
            tipo: TipoMovimiento.SalidaPorDevolucionAProveedor,
            empresaId: empresaId,
            fechaMovimiento: request.FechaMovimiento);

        typeof(MovimientoInventario).GetProperty("ProveedorId")!.SetValue(movimiento, dev.ProveedorId);
        typeof(MovimientoInventario).GetProperty("RecepcionOrigenId")!.SetValue(movimiento, dev.RecepcionOrigenId);
        typeof(MovimientoInventario).GetProperty("OcId")!.SetValue(movimiento, dev.OrdenCompraOrigenId);
        typeof(MovimientoInventario).GetProperty("Motivo")!.SetValue(movimiento, dev.Motivo);

        // C7.2b: bin elegido al registrar, por línea de devolución.
        var binPorLinea = (request.Bins ?? Array.Empty<DevolucionProveedorSalidaLineaBin>())
            .ToDictionary(b => b.LineaDevolucionId, b => b.UbicacionId);

        // PR6a (almacén-por-línea): la ÚNICA (es_default) del sub-almacén como
        // fallback cuando la línea no trae bin explícito. Sube al app-layer el
        // enrutamiento que hacía el trigger y que 6a le retira: tras el
        // SET NOT NULL, `lineas_movimiento.ubicacion_id` ya no admite NULL, así
        // que este flujo (8.B, `request.Bins` es opcional) debe resolver el bin
        // aquí. Si el sub no tiene ÚNICA, es catálogo faltante → fallo ruidoso,
        // no un NULL que reventaría opaco en el constraint.
        var ubicacionDefaultId = await _db.Ubicaciones.AsNoTracking()
            .Where(u => u.SubAlmacenId == request.SubAlmacenId && u.EsDefault)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // GAP-5: resolver la línea de OC por artículo contra la OC de
        // origen (mismo criterio que la recepción, paso 2 del resolver).
        // Compras usa LineaOcId para decrementar CantidadRecibida; si no
        // hay OC de origen o el artículo no matchea, viaja NULL y Compras
        // omite la línea.
        IReadOnlyList<OcLineaLectura> lineasOc = Array.Empty<OcLineaLectura>();
        if (dev.OrdenCompraOrigenId is Guid ordenCompraOrigenId)
        {
            var oc = await _ocPort.ObtenerAsync(ordenCompraOrigenId, cancellationToken);
            lineasOc = oc?.Lineas ?? Array.Empty<OcLineaLectura>();
        }

        var posicion = 1;
        var payload = new List<LineaDevolucionProveedorPayload>(dev.Lineas.Count);
        decimal montoTotal = 0m;
        foreach (var linea in dev.Lineas.OrderBy(l => l.Posicion))
        {
            var movLineaId = Guid.CreateVersion7();
            var ubicacionLinea = binPorLinea.TryGetValue(linea.Id, out var ubi)
                ? ubi
                : ubicacionDefaultId ?? throw new BusinessRuleException(
                    "SUBALMACEN_SIN_UBICACION_DEFAULT",
                    $"El sub-almacén '{request.SubAlmacenId}' no tiene ubicación default (ÚNICA); " +
                    $"no se puede resolver el bin de la devolución para el artículo '{linea.ArticuloId}'. " +
                    "Da de alta la ubicación default del sub-almacén antes de registrar la devolución.");
            var movLinea = new LineaMovimiento(
                id: movLineaId,
                movimientoId: movId,
                posicion: posicion++,
                articuloId: linea.ArticuloId,
                cantidad: linea.Cantidad,
                unidadMedida: linea.UnidadMedida,
                costoUnitarioMxn: linea.CostoUnitarioMxn, // A10 — snapshot recepción origen
                ubicacionId: ubicacionLinea);
            movimiento.AgregarLinea(movLinea);

            payload.Add(new LineaDevolucionProveedorPayload(
                LineaDevolucionId: linea.Id,
                LineaRecepcionOrigenId: linea.LineaRecepcionOrigenId,
                LineaOcId: lineasOc.FirstOrDefault(l => l.ArticuloId == linea.ArticuloId)?.LineaId,
                ArticuloId: linea.ArticuloId,
                UnidadMedida: linea.UnidadMedida,
                Cantidad: linea.Cantidad,
                CostoUnitarioMxn: linea.CostoUnitarioMxn,
                MontoTotalMxn: linea.MontoTotalMxn));
            montoTotal += linea.MontoTotalMxn;
        }

        // Folio + Registrar.
        var anio = request.FechaMovimiento.Year;
        var prefijo = TipoMovimiento.SalidaPorDevolucionAProveedor.PrefijoFolio();
        var sec = await _db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, cancellationToken);
        if (sec is null)
        {
            sec = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            _db.FolioSecuenciasMovimiento.Add(sec);
        }
        var folio = FolioMovimiento.Construir(
            TipoMovimiento.SalidaPorDevolucionAProveedor, anio, sec.Incrementar());

        movimiento.Registrar(folio, registradoPor);
        _db.Movimientos.Add(movimiento);

        // Marcar agregado DevolucionAProveedor como Registrada.
        dev.MarcarRegistrada(movId, folio.Valor);

        // Publicar OcDevolucionRegistradaEvent.
        await _events.PublishAsync(new OcDevolucionRegistradaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            DevolucionId: dev.Id,
            FolioMovimiento: folio.Valor,
            ProveedorId: dev.ProveedorId,
            RecepcionOrigenId: dev.RecepcionOrigenId,
            FacturaProveedorOrigenId: dev.FacturaProveedorOrigenId,
            OrdenCompraOrigenId: dev.OrdenCompraOrigenId,
            Motivo: dev.Motivo,
            MontoTotalMxn: Math.Round(montoTotal, 2),
            Lineas: payload), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new RegistrarSalidaDevolucionAProveedorResponse(dev.Id, movId, folio.Valor);
    }
}
