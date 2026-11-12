using Millet.Contabilidad.Application.PublicPorts;
using MediatR;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Application.PublicPorts;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ComercioExterior.Application;
public sealed record PartidaCostoDatos(Guid PartidaId, Guid? ContenedorId, string Descripcion, decimal ValorAduanaMxn, decimal PesoKg, string Pedimento);
public sealed record AnticipoDatos(Guid AgenteId, string Pedimento, ImpuestosPedimento Impuestos, DateOnly Fecha, string? ReferenciaPago, bool CuentaPeca = false, Guid? SoporteCalculoId = null, decimal? Importe = null);
public sealed record ComprobacionAnticipoDatos(ImpuestosPedimento Impuestos, Guid PedimentoPagadoId, Guid ComprobantePagoId, DateOnly Fecha, IReadOnlyCollection<Guid>? GastoIds = null, string? Motivo = null);
public sealed record ResolucionAnticipoDatos(string Tipo, decimal Importe, DateOnly Fecha, string Referencia, Guid EvidenciaId, Guid? AnticipoDestinoId = null, int? VersionDestino = null);
/// <param name="GrupoContable">M-169: grupo contable del gasto de importación; null conserva el actual.</param>
public sealed record ConceptoGastoDatos(BaseDistribucion Importacion, BaseDistribucion Exportacion, bool? RequiereOrdenServicio = null,
    GrupoContableGasto? GrupoContable = null);
public sealed record FinanzasDetalle(int Version, string EstadoFinanciero, IReadOnlyList<string> Pendientes,
    IReadOnlyList<ConceptoGasto> Conceptos, IReadOnlyCollection<GastoExpediente> Gastos, IReadOnlyCollection<PartidaCosto> Bases,
    IReadOnlyList<BasePartidaCosto> Partidas, IReadOnlyCollection<DistribucionGasto> Distribuciones, IReadOnlyCollection<AnticipoAgente> Anticipos, IReadOnlyDictionary<Guid, string> Nombres)
{
    public IReadOnlyList<Millet.Tesoreria.Application.PublicPorts.AutorizacionAnticipoCe> Autorizaciones { get; init; } = [];
    /// <summary>M-169: false (por omisión, Fernando 4-nov) = el gasto va a gasto de importación y no cambia el costo del inventario.</summary>
    public bool IncrementablesAlInventario { get; init; }
    public IReadOnlyList<OrdenServicioResumen> OrdenesServicio => Gastos.Where(g => g.OrdenServicioId != null)
        .GroupBy(g => g.OrdenServicioId!.Value).Select(g =>
        {
            var primero = g.First();
            var facturado = g.Sum(x => x.Moneda == x.OrdenServicioMoneda ? x.Importe : decimal.Round(x.Importe * x.TipoCambio / x.OrdenServicioTipoCambio!.Value, 2, MidpointRounding.AwayFromZero));
            return new OrdenServicioResumen(g.Key, primero.OrdenServicioFolio!, primero.OrdenServicioMoneda!,
                primero.OrdenServicioTotal!.Value, primero.OrdenServicioAsignado!.Value, facturado,
                facturado - primero.OrdenServicioAsignado.Value);
        }).ToArray();
}
public sealed record OrdenServicioResumen(Guid Id, string Folio, string Moneda, decimal Total, decimal Asignado, decimal Facturado, decimal Diferencia);
public sealed record MutarFinanzasCommand(Guid ExpedienteId, int Version, string Accion, Guid? ElementoId = null,
    DatosGasto? Gasto = null, PartidaCostoDatos? Partida = null, AnticipoDatos? Anticipo = null,
    ComprobacionAnticipoDatos? Comprobacion = null, ResolucionAnticipoDatos? Resolucion = null, ConceptoGastoDatos? Concepto = null,
    DistribuirDatos? Distribuir = null, ReversaComprobacionDatos? Reversa = null) : IRequest<FinanzasDetalle>;
/// <summary>M-327: motivo para solicitar la reversa de una comprobación o para no autorizarla.</summary>
public sealed record ReversaComprobacionDatos(string? Motivo);
/// <summary>H1 paso 4 (86e3z5w25): área (Dim2) y equipo (Dim3) que se capturan al distribuir; obligatorios si la cuenta los exige (M-450).</summary>
public sealed record DistribuirDatos(Guid? AreaId, Guid? EquipoId);
/// <remarks>H1 paso 4 (aditivo): <c>CentroCostoId</c> = área o equipo capturado al distribuir; Almacén lo pasa a Contabilidad.
/// M-169 (aditivo): <c>GastosPorGrupo</c> = ajuste de la versión por grupo contable; un evento sin él va todo a gasto de importación.</remarks>
public sealed record ExpedienteCostoDistribuidoEvent(Guid Empresa, DateTimeOffset Fecha, Guid ExpedienteId, Guid SucursalId,
    int NumeroVersion, IReadOnlyList<CostoPartidaDistribuido> Partidas)
    : IntegrationEvent("comercio_exterior.expediente.costo-distribuido.v1", Empresa, Fecha)
{
    public Guid? CentroCostoId { get; init; }
    public IReadOnlyList<GastoGrupoDistribuido> GastosPorGrupo { get; init; } = [];
}

/// <summary>Qué exige la cuenta del gasto de importación (regla C-03) en la sucursal del expediente.</summary>
public sealed record DimensionesDistribucion(Guid SucursalId, bool ExigeArea, bool ExigeEquipo, string? CuentaCodigo);

/// <summary>
/// H1 paso 4 (86e3z5w25): la póliza del costo aplicado (<c>almacen.expediente.costo-aplicado.v1</c>) carga la parte de piezas ya
/// vendidas a gasto de importación (501, regla C-03). El área y el equipo se capturan al distribuir: Contabilidad dice qué exige la
/// cuenta, ofrece los centros de la sucursal del expediente y valida lo capturado. M-450: sin el dato exigido la distribución no
/// procede.
/// </summary>
public sealed class DimensionesDistribucionServicio(ExpedienteServicio servicio, IDimensionesCapturaPort dimensiones, IClock clock)
{
    public const string TipoEvento = "almacen.expediente.costo-aplicado.v1";
    public const string Campo = "GastoImportacion";
    /// <summary>
    /// M-169: campos de las líneas de cargo a gasto de la plantilla (gasto sin grupo y uno por grupo contable). El área y el equipo
    /// son uno solo para el expediente: se exige lo que pida cualquiera de esas cuentas.
    /// </summary>
    public static readonly IReadOnlyList<string> Campos =
        [Campo, "GastoHonorariosPf", "GastoHonorariosPm", "GastoFletesManiobras", "GastoAlmacenajes", "GastoOtros"];

    public static MovimientoCaptura Movimiento(Expediente e, DateOnly fecha, string campo = Campo) => new(e.EmpresaId, e.SucursalId, TipoEvento, campo, null, fecha);

    public async Task<DimensionesDistribucion> ExigenciasAsync(Guid id, CancellationToken ct)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorGastosDistribuir, ct);
        var e = await servicio.CargarAsync(id, false, ct);
        List<ExigenciaDimensiones> exigencias = [];
        foreach (var campo in Campos) exigencias.Add(await dimensiones.ExigenciasAsync(Movimiento(e, clock.HoyLocal(), campo), ct));
        var cuenta = exigencias.FirstOrDefault(x => x.ExigeArea || x.ExigeEquipo)?.CuentaCodigo ?? exigencias.FirstOrDefault(x => x.CuentaCodigo is not null)?.CuentaCodigo;
        return new(e.SucursalId, exigencias.Any(x => x.ExigeArea), exigencias.Any(x => x.ExigeEquipo), cuenta);
    }

    public async Task<IReadOnlyList<Millet.Contabilidad.Application.Dimensiones.CentroOpcionDto>> CentrosAsync(Guid id, bool equipos, Guid? areaId,
        string? q, int limite, CancellationToken ct)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorGastosDistribuir, ct);
        var e = await servicio.CargarAsync(id, false, ct);
        return await dimensiones.CentrosAsync(e.EmpresaId, e.SucursalId, equipos, areaId, q, limite, ct);
    }

    /// <summary>
    /// Centro que viaja en el evento (el equipo si se eligió; si no, el área). Un dato inválido rechaza la distribución.
    /// M-450 (86e3zr7ft): si la cuenta del gasto de importación exige área o equipo (regla C-03) y no se capturaron, tampoco procede.
    /// </summary>
    public async Task<Guid?> CentroAsync(Expediente e, DistribuirDatos? datos, CancellationToken ct)
    {
        Guid? centro = null;
        foreach (var campo in Campos)
        {
            var r = await dimensiones.ValidarAsync(Movimiento(e, clock.HoyLocal(), campo), datos?.AreaId, datos?.EquipoId, ct);
            if (r.Error is not null) throw new BusinessRuleException("CE_DIMENSION_INVALIDA", r.Error);
            if (r.Faltante is not null) throw new BusinessRuleException("CE_DIMENSION_FALTANTE", r.Faltante);
            centro ??= r.CentroCostoId;
        }
        return centro;
    }
}

public sealed record AnticipoAgenteSaldoTransferidoEvent(Guid Empresa, DateTimeOffset OcurridoEn,
    Guid ExpedienteId, string FolioExpediente, Guid AnticipoAgenteId, int ResolucionIndice,
    Guid AgenteId, decimal ImporteMxn, DateOnly Fecha, string Referencia)
    : IntegrationEvent("comercio_exterior.anticipo-agente.saldo-transferido.v1", Empresa, OcurridoEn);

public sealed class FinanzasServicio(ComercioExteriorDbContext db, ExpedienteServicio servicio,
    IRecepcionesCostoReadPort recepciones, IClock clock, ICatalogosCeReadPort catalogos, IOptions<IncrementablesImportacionOptions> incrementables, Millet.Tesoreria.Application.PublicPorts.IAutorizacionPagoCeReadPort autorizaciones, IComprobacionAnticipoCeReadPort aplicaciones)
{
    /// <summary>Fernando 4-nov: los incrementables de importación no van a inventario salvo que se active la versión posterior.</summary>
    public bool IncrementablesAlInventario => incrementables.Value.AlInventario;

    public async Task<FinanzasDetalle> ObtenerAsync(Guid id, CancellationToken ct)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesLeer, ct);
        return await DetalleAsync(await servicio.CargarAsync(id, false, ct), ct);
    }
    public async Task<IReadOnlyList<ConceptoGasto>> ConceptosAsync(Guid empresa, CancellationToken ct)
    {
        var conceptos = await db.ConceptosGasto.Where(c => c.EmpresaId == empresa).OrderBy(c => c.Codigo).ToListAsync(ct);
        return conceptos;
    }
    public async Task<IReadOnlyList<BasePartidaCosto>> PartidasAsync(Expediente e, CancellationToken ct)
    {
        if (e.Tipo == TipoExpediente.Importacion)
        {
            List<RecepcionCostoLectura> recibidas = [];
            foreach (var ocId in e.OrdenesCompraIds)
                recibidas.AddRange(await recepciones.ListarAsync(e.EmpresaId, e.SucursalId, ocId, ct));
            return recibidas.Select(r =>
            {
                var b = e.PartidasCosto.SingleOrDefault(p => p.Id == r.PartidaId);
                return new BasePartidaCosto(r.PartidaId, b?.ContenedorId, b?.Descripcion ?? r.Descripcion, b?.ValorAduanaMxn ?? 0,
                    b?.PesoKg ?? 0, r.Cantidad, null, r.OcId, r.RecepcionId, r.ArticuloId, r.UbicacionId, r.Remanente, r.CostoPromedioMxn, r.ExistenciaTotal);
            }).ToArray();
        }
        return e.Partidas.Where(p => p.Estado != EstadoPartida.Liberada && e.Contenedor(p.ContenedorId).Estado == EstadoContenedor.Activo).Select(p =>
        {
            var b = e.PartidasCosto.SingleOrDefault(b => b.Id == p.Id);
            return new BasePartidaCosto(p.Id, p.ContenedorId, b?.Descripcion ?? "Partida " + p.Posicion,
                b?.ValorAduanaMxn ?? 0, b?.PesoKg ?? 0, p.Cantidad, p.ClienteId);
        }).ToArray();
    }
    public async Task<FinanzasDetalle> DetalleAsync(Expediente e, CancellationToken ct)
    {
        var terceros = await catalogos.NombresTercerosAsync(e.Gastos.Select(g => g.ProveedorId).Concat(e.Anticipos.Select(a => a.AgenteId)).Concat(e.Partidas.Select(p => p.ClienteId)), ct);
        var firmas = await autorizaciones.ObtenerAsync(e.Anticipos.Select(a => a.Id).ToArray(), ct);
        var errores = await aplicaciones.ErroresAsync(e.Anticipos.Select(a => a.Id).ToArray(), ct);
        var usuarios = await catalogos.NombresAsync(firmas.SelectMany(a => new[] { a.Nivel1UsuarioId, a.Nivel2UsuarioId }).OfType<Guid>().Concat(e.Gastos.Where(g => g.TipoCambioModificadoPor is not null).Select(g => g.TipoCambioModificadoPor!.Value))
            // M-327: quién pidió y quién firmó cada reversa de comprobación.
            .Concat(e.Anticipos.SelectMany(a => a.ReversasComprobacion.SelectMany(r => new[] { r.SolicitadoPor, r.FirmadoPor })))
            .Concat(e.Anticipos.Where(a => a.ReversaPendiente is not null).Select(a => a.ReversaPendiente!.SolicitadoPor)).Distinct(), ct);
        return new(e.Version, e.EstadoFinanciero,
        e.MotivosFinancieros().Concat(errores).ToArray(), await ConceptosAsync(e.EmpresaId, ct), e.Gastos, e.PartidasCosto, await PartidasAsync(e, ct), e.Distribuciones, e.Anticipos,
            terceros.Concat(usuarios).GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.First().Value)) { Autorizaciones = firmas, IncrementablesAlInventario = IncrementablesAlInventario };
    }
    public async Task<DistribucionGasto> PreviaAsync(Guid id, int version, CancellationToken ct)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorGastosDistribuir, ct);
        var e = await servicio.CargarAsync(id, true, ct);
        if (e.Version != version) throw new ConcurrencyException("Expediente", id);
        return await PrepararAsync(e, ct);
    }
    /// <summary>M-169: la distribución separa los honorarios aduanales por el tipo de persona del proveedor de cada gasto.</summary>
    public async Task<DistribucionGasto> PrepararAsync(Expediente e, CancellationToken ct) =>
        e.PrepararDistribucion(await ConceptosAsync(e.EmpresaId, ct), await PartidasAsync(e, ct), servicio.Actor, clock.UtcNow, IncrementablesAlInventario,
            await catalogos.ProveedoresPersonaFisicaAsync(e.Gastos.Select(g => g.ProveedorId), ct));
}

public sealed class MutarFinanzasValidator : AbstractValidator<MutarFinanzasCommand>
{
    public MutarFinanzasValidator()
    {
        RuleFor(x => x.ExpedienteId).NotEmpty(); RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Resolucion!.Tipo).Must(t => t is "compensar" or "devolucion" or "pago-complementario" or "saldo-al-agente")
            .When(x => x.Resolucion is not null);
        RuleFor(x => x.Accion).Must(x => x is "gasto" or "partida" or "distribuir" or "anticipo" or "comprobar" or "resolver" or "corregir" or "cancelar-anticipo" or "concepto" or "inicializar-conceptos"
            or "revertir-comprobacion" or "autorizar-reversa" or "rechazar-reversa");
    }
}
public sealed class MutarFinanzasHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, FinanzasServicio finanzas,
    ICatalogosCeReadPort catalogos, IFacturaGastoReadPort facturas, ITipoCambioGastoReadPort cambio, IClock clock,
    IIntegrationEventPublisher eventos, OrdenesServicioServicio ordenes, IPeriodoContableConsultaPort periodos, ISender mediator, Millet.Tesoreria.Application.PublicPorts.IAutorizacionPagoCeReadPort pagosCe,
    DimensionesDistribucionServicio dimensiones) : IRequestHandler<MutarFinanzasCommand, FinanzasDetalle>
{
    public async Task<FinanzasDetalle> Handle(MutarFinanzasCommand request, CancellationToken cancellationToken)
    {
        var permiso = request.Accion switch
        {
            "resolver" => request.Resolucion?.Tipo == "devolucion" ? PermisosCanonicos.ComercioExteriorAnticiposGestionar : PermisosCanonicos.TesoreriaPagosCeAutorizarDireccion,
            "anticipo" or "comprobar" or "corregir" or "cancelar-anticipo" => PermisosCanonicos.ComercioExteriorAnticiposGestionar,
            "distribuir" => PermisosCanonicos.ComercioExteriorGastosDistribuir,
            // M-327: Comercio exterior solicita; el Contador General autoriza o no (Millet, 10-nov-2026).
            "revertir-comprobacion" => PermisosCanonicos.ComercioExteriorAnticiposRevertirComprobacionSolicitar,
            "autorizar-reversa" or "rechazar-reversa" => PermisosCanonicos.ComercioExteriorAnticiposRevertirComprobacionAutorizar,
            _ => PermisosCanonicos.ComercioExteriorGastosRegistrar
        };
        await servicio.PermisoAsync(permiso, cancellationToken);
        await using var tx = request.Accion is "gasto" or "comprobar" or "corregir" or "autorizar-reversa" ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        // El saldo compartido abarca expedientes distintos; el ETag de un expediente no lo protege.
        if (tx is not null) await db.Database.ExecuteSqlRawAsync(request.Accion == "gasto" ? "SELECT pg_advisory_xact_lock(7060601)" : "SELECT pg_advisory_xact_lock(22574482598217551)", cancellationToken);
        var e = await servicio.CargarAsync(request.ExpedienteId, true, cancellationToken);
        if (e.Version != request.Version) throw new ConcurrencyException("Expediente", e.Id);
        e.ExigirAbierto();
        var id = request.ElementoId ?? Guid.CreateVersion7();
        AnticipoAgente Anticipo() => e.Anticipos.SingleOrDefault(a => a.Id == id) ?? throw new EntityNotFoundException("CE_ANTICIPO_NO_ENCONTRADO", "No existe el anticipo.");
        async Task Evidencia(Guid adjunto)
        {
            if (!(await catalogos.AdjuntosVigentesAsync(e.Id, cancellationToken)).Contains(adjunto))
                throw new BusinessRuleException("CE_EVIDENCIA_REQUERIDA", "Seleccione evidencia vigente del expediente.");
        }
        async Task PublicarGastosAsync()
        {
            var anticipo = Anticipo();
            await eventos.PublishAsync(new AnticipoGastosAplicadosEvent(e.EmpresaId, clock.UtcNow, anticipo.Id,
                anticipo.AgenteId, e.Folio, anticipo.RevisionComprobacion, e.Gastos.Where(g => g.AnticipoAgenteId == id)
                    .Select(g => new GastoAnticipoAplicado(g.Id, g.FacturaProveedorId!.Value, g.Importe)).ToArray()), cancellationToken);
        }
        switch (request.Accion)
        {
            case "cancelar-anticipo":
                await mediator.Send(new Millet.Tesoreria.Application.Pagos.SolicitarCancelacionPasivoCommand(Anticipo().Id, request.Comprobacion?.Motivo ?? ""), cancellationToken);
                e.MarcarCambioFinanciero();
                break;
            case "revertir-comprobacion":
                Anticipo().SolicitarReversaComprobacion(request.Reversa?.Motivo ?? "", servicio.Actor, clock.UtcNow);
                e.MarcarCambioFinanciero();
                break;
            case "rechazar-reversa":
                Anticipo().RechazarReversaComprobacion(servicio.Actor, clock.UtcNow, request.Reversa?.Motivo ?? "");
                e.MarcarCambioFinanciero();
                break;
            case "autorizar-reversa":
            {
                var revertir = Anticipo();
                if (revertir.ReversaPendiente is not null) await ValidacionesFinancieras.PeriodoAbiertoAsync(periodos, clock.HoyLocal(), cancellationToken);
                var reversa = revertir.AutorizarReversaComprobacion(servicio.Actor, clock.UtcNow, e.Gastos.Where(g => g.AnticipoAgenteId == id).ToArray());
                if (reversa is null) break; // Ya aplicada: una reversa se aplica una sola vez.
                e.MarcarCambioFinanciero();
                // Nueva revisión sin gastos: Cuentas por pagar deshace la aplicación del anticipo a las facturas del agente.
                await PublicarGastosAsync();
                await eventos.PublishAsync(new AnticipoComprobacionRevertidaEvent(e.EmpresaId, clock.UtcNow, e.Id, e.Folio, e.SucursalId,
                    revertir.Id, reversa.SolicitudId, revertir.AgenteId, reversa.SaldoAFavorMxn, reversa.GastosLiberadosMxn, clock.HoyLocal(),
                    reversa.Motivo, reversa.SolicitadoPor, reversa.FirmadoPor), cancellationToken);
                break;
            }
            case "inicializar-conceptos":
                if (!await db.ConceptosGasto.AnyAsync(c => c.EmpresaId == e.EmpresaId, cancellationToken)) db.ConceptosGasto.AddRange(ConceptoGasto.CrearCatalogo(e.EmpresaId));
                e.MarcarCambioFinanciero();
                break;
            case "concepto":
                var config = request.Concepto ?? throw new BusinessRuleException("CE_CONCEPTO_INVALIDO", "Capture las bases.");
                var conceptoEditar = await db.ConceptosGasto.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new EntityNotFoundException("CE_CONCEPTO_INVALIDO", "No existe el concepto.");
                conceptoEditar.Configurar(config.Importacion, config.Exportacion, config.RequiereOrdenServicio, config.GrupoContable);
                // El catálogo es de empresa: no se permite alterar expedientes de sucursales fuera del alcance del actor.
                var afectados = await db.Expedientes.Where(x => x.Gastos.Any(g => g.ConceptoId == id)).ToListAsync(cancellationToken);
                foreach (var afectado in afectados.Where(x => x.Estado == EstadoExpediente.Abierto))
                { await servicio.SucursalAsync(afectado.SucursalId, true, cancellationToken); afectado.MarcarCambioFinanciero(true); }
                if (!afectados.Any(x => x.Id == e.Id)) e.MarcarCambioFinanciero();
                break;
            case "gasto":
                var g = request.Gasto ?? throw new BusinessRuleException("CE_GASTO_INVALIDO", "Capture el gasto.");
                if (string.IsNullOrWhiteSpace(g.Factura)) throw new BusinessRuleException("CE_FACTURA_REQUERIDA", "Capture el UUID de la factura o la referencia del impuesto PECA.");
                if (g.FechaFactura > clock.HoyLocal()) throw new BusinessRuleException("CE_FECHA_INVALIDA", "La fecha de factura no puede ser futura.");
                await catalogos.ValidarParticipanteAsync(g.ProveedorId, null, cancellationToken);
                var concepto = await db.ConceptosGasto.SingleOrDefaultAsync(c => c.Id == g.ConceptoId, cancellationToken) ?? throw new BusinessRuleException("CE_CONCEPTO_INVALIDO", "Seleccione un concepto del catálogo.");
                var factura = await facturas.BuscarAsync(g.ProveedorId, g.Factura, g.FacturaProveedorId, cancellationToken);
                ValidacionesFinancieras.FacturaRequerida(g with { FacturaProveedorId = factura?.Id }, concepto);
                if ((!g.ImpuestoPeca && factura is null) || (g.FacturaProveedorId is not null && factura is null) || (factura is not null && (factura.SucursalId != e.SucursalId || factura.Moneda != g.Moneda || factura.Fecha != g.FechaFactura)))
                    throw new BusinessRuleException("CE_FACTURA_INVALIDA", "La factura debe corresponder al proveedor, sucursal, moneda y fecha del gasto.");
                if (factura is not null)
                {
                    if (!g.ImpuestoPeca && !Guid.TryParse(factura.Referencia, out _))
                        throw new BusinessRuleException("CE_UUID_REQUERIDO", "La factura de Cuentas por Pagar debe tener UUID fiscal.");
                    await ValidacionesFinancieras.FacturaUnicaAsync(db, e.EmpresaId, factura.Id, id, cancellationToken);
                }
                var resuelto = await TipoCambioGastoResolver.ResolverAsync(cambio, g.Moneda, g.FechaFactura, g.FuenteTipoCambio,
                    g.TipoCambioUsado, factura?.TipoCambio, cancellationToken);
                var (tc, fuenteTc, sugerido) = (resuelto.TipoCambio, resuelto.Fuente, resuelto.Sugerido);
                var orden = await ordenes.ValidarAsync(e, g, concepto, tc, cancellationToken);
                if (orden?.Moneda == "USD" && g.Moneda == "MXN")
                    orden = orden with { TipoCambio = await TipoCambioGastoResolver.ObligatorioAsync(cambio, g.FechaFactura, cancellationToken) };
                if (request.ElementoId is not null && !e.Gastos.Any(x => x.Id == id)) throw new EntityNotFoundException("CE_GASTO_NO_ENCONTRADO", "No existe el gasto.");
                e.GuardarGasto(id, g with { FacturaProveedorId = factura?.Id }, concepto, tc, fuenteTc, orden, sugerido, g.NotaTipoCambio, servicio.Actor);
                break;
            case "partida":
                var p = request.Partida ?? throw new BusinessRuleException("CE_PARTIDA_COSTO_INVALIDA", "Capture las bases de la partida.");
                var origen = (await finanzas.PartidasAsync(e, cancellationToken)).SingleOrDefault(x => x.PartidaId == p.PartidaId)
                    ?? throw new BusinessRuleException("CE_PARTIDA_COSTO_INVALIDA", "Seleccione una partida recibida de la OC o embarcada en el expediente.");
                if (e.Tipo == TipoExpediente.Exportacion && p.ContenedorId != origen.ContenedorId)
                    throw new BusinessRuleException("CE_CONTENEDOR_INVALIDO", "El contenedor debe corresponder a la partida.");
                e.GuardarPartidaCosto(p.PartidaId, p.ContenedorId, p.Descripcion, p.ValorAduanaMxn, p.PesoKg, p.Pedimento);
                break;
            case "distribuir":
                await ValidacionesFinancieras.PeriodoAbiertoAsync(periodos, clock.HoyLocal(), cancellationToken);
                var centro = e.Tipo == TipoExpediente.Importacion ? await dimensiones.CentroAsync(e, request.Distribuir, cancellationToken) : null;
                var distribucion = await finanzas.PrepararAsync(e, cancellationToken);
                e.ConfirmarDistribucion(distribucion);
                if (e.Tipo == TipoExpediente.Importacion)
                    await eventos.PublishAsync(new ExpedienteCostoDistribuidoEvent(e.EmpresaId, clock.UtcNow, e.Id, e.SucursalId, distribucion.NumeroVersion, distribucion.Partidas)
                        { CentroCostoId = centro, GastosPorGrupo = distribucion.GastosPorGrupo }, cancellationToken);
                break;
            case "anticipo":
                var a = request.Anticipo ?? throw new BusinessRuleException("CE_ANTICIPO_INVALIDO", "Capture el anticipo.");
                if (a.Fecha > clock.HoyLocal()) throw new BusinessRuleException("CE_FECHA_INVALIDA", "La fecha no puede ser futura.");
                await catalogos.ValidarParticipanteAsync(a.AgenteId, null, cancellationToken);
                await Evidencia(a.SoporteCalculoId ?? Guid.Empty);
                var solicitado = e.RegistrarAnticipo(id, a.AgenteId, a.Pedimento, a.Impuestos ?? throw new BusinessRuleException("CE_IMPUESTOS_INVALIDOS", "Capture IVA, IGI y PRV."), a.Fecha, null, a.CuentaPeca);
                solicitado.Solicitar(a.SoporteCalculoId!.Value, a.Importe ?? throw new BusinessRuleException("CE_IMPORTE_REQUERIDO", "Capture el importe solicitado del anticipo."));
                await eventos.PublishAsync(new AnticipoAgenteSolicitadoEvent(e.EmpresaId, clock.UtcNow, e.Id, e.Folio,
                    e.SucursalId, e.OcId, e.Tipo, solicitado.Id, solicitado.AgenteId, solicitado.Anticipado, a.Fecha, a.SoporteCalculoId.Value), cancellationToken);
                break;
            case "comprobar":
            case "corregir":
                if (Anticipo().SoporteCalculoId is not null && await pagosCe.PagadoAsync(id, cancellationToken) != Anticipo().Anticipado)
                    throw new BusinessRuleException("CE_ANTICIPO_SIN_PAGO", "El anticipo aún no se paga en Tesorería o su pago fue revertido.");
                var c = request.Comprobacion ?? throw new BusinessRuleException("CE_COMPROBACION_INVALIDA", "Capture los impuestos pagados.");
                if (c.Fecha > clock.HoyLocal()) throw new BusinessRuleException("CE_FECHA_INVALIDA", "La fecha no puede ser futura.");
                await Evidencia(c.PedimentoPagadoId); await Evidencia(c.ComprobantePagoId);
                var gastoIds = c.GastoIds ?? [];
                var gastos = e.Gastos.Where(g => gastoIds.Contains(g.Id)).ToArray();
                if (gastos.Length != gastoIds.Count || gastoIds.Distinct().Count() != gastoIds.Count)
                    throw new BusinessRuleException("CE_GASTO_AGENTE_INVALIDO", "Seleccione gastos únicos del expediente.");
                foreach (var gastoAplicar in gastos.Where(g => g.AnticipoAgenteId != id))
                {
                    var lectura = await facturas.BuscarAsync(gastoAplicar.ProveedorId, gastoAplicar.Factura, gastoAplicar.FacturaProveedorId, cancellationToken);
                    if (lectura is null) throw new BusinessRuleException("CE_GASTO_SIN_FACTURA", "Para aplicar un gasto al anticipo, ligue antes la factura del agente.");
                    if (lectura.ImportePagado > 0 || lectura.FechaPago is not null)
                        throw new BusinessRuleException("CE_FACTURA_YA_PAGADA", $"La factura {lectura.Referencia} ya fue pagada el {lectura.FechaPago?.ToString("dd-MMM", System.Globalization.CultureInfo.GetCultureInfo("es-MX"))}; no se puede aplicar al anticipo.");
                    if (lectura.SaldoPendiente < gastoAplicar.Importe)
                        throw new BusinessRuleException("CE_FACTURA_SALDO_INSUFICIENTE", $"La factura {lectura.Referencia} no tiene saldo suficiente para aplicar al anticipo.");
                }
                if (request.Accion == "corregir")
                {
                    Anticipo().Corregir(c.Impuestos ?? throw new BusinessRuleException("CE_IMPUESTOS_INVALIDOS", "Capture los impuestos pagados."), c.PedimentoPagadoId, c.ComprobantePagoId, c.Fecha, servicio.Actor,
                        c.Motivo ?? "", e.Gastos.Where(g => g.AnticipoAgenteId == id).ToArray(), gastos);
                    await PublicarGastosAsync();
                    e.MarcarCambioFinanciero(); break;
                }
                Anticipo().Comprobar(c.Impuestos ?? throw new BusinessRuleException("CE_IMPUESTOS_INVALIDOS", "Capture los impuestos pagados."), c.PedimentoPagadoId, c.ComprobantePagoId, c.Fecha, servicio.Actor);
                Anticipo().AplicarGastos(gastos); e.MarcarCambioFinanciero();
                await PublicarGastosAsync();
                break;
            case "resolver":
                var s = request.Resolucion ?? throw new BusinessRuleException("CE_RESOLUCION_INVALIDA", "Capture la resolución.");
                if (s.Fecha > clock.HoyLocal()) throw new BusinessRuleException("CE_FECHA_INVALIDA", "La fecha no puede ser futura.");
                await Evidencia(s.EvidenciaId);
                AnticipoAgente? destino = null;
                if (s.Tipo == "compensar")
                {
                    var destinoId = await db.AnticiposAgente.Where(a => a.Id == s.AnticipoDestinoId).Select(a => (Guid?)a.ExpedienteId).SingleOrDefaultAsync(cancellationToken)
                        ?? throw new EntityNotFoundException("CE_ANTICIPO_NO_ENCONTRADO", "No existe el anticipo destino.");
                    var expedienteDestino = await servicio.CargarAsync(destinoId, true, cancellationToken);
                    if (expedienteDestino.Version != s.VersionDestino) throw new ConcurrencyException("Expediente", destinoId);
                    expedienteDestino.ExigirAbierto(); destino = expedienteDestino.Anticipos.Single(a => a.Id == s.AnticipoDestinoId);
                    expedienteDestino.MarcarCambioFinanciero();
                }
                var anticipo = Anticipo();
                if (s.Tipo == "devolucion")
                {
                    var devolucionId = anticipo.AprobarDevolucion(s.Importe, s.Fecha, s.Referencia, s.EvidenciaId, servicio.Actor);
                    await eventos.PublishAsync(new AnticipoAgenteDevolucionAprobadaEvent(e.EmpresaId, clock.UtcNow,
                        e.Id, e.Folio, e.SucursalId, anticipo.Id, anticipo.AgenteId, devolucionId, s.Importe, servicio.Actor, anticipo.FechaComprobacion), cancellationToken);
                    e.MarcarCambioFinanciero(); break;
                }
                anticipo.Resolver(s.Tipo, s.Importe, s.Fecha, s.Referencia, s.EvidenciaId, servicio.Actor, destino); e.MarcarCambioFinanciero();
                if (s.Tipo == "saldo-al-agente")
                    await eventos.PublishAsync(new AnticipoAgenteSaldoTransferidoEvent(e.EmpresaId, clock.UtcNow,
                        e.Id, e.Folio, anticipo.Id, anticipo.Resoluciones.Count - 1, anticipo.AgenteId,
                        s.Importe, s.Fecha, s.Referencia.Trim()), cancellationToken);
                break;
            default: throw new BusinessRuleException("CE_ACCION_INVALIDA", "Acción desconocida.");
        }
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
        return await finanzas.DetalleAsync(e, cancellationToken);
    }
}
