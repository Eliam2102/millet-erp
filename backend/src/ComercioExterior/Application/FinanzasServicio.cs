using MediatR;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
public sealed record AnticipoDatos(Guid AgenteId, string Pedimento, ImpuestosPedimento Impuestos, DateOnly Fecha, string? ReferenciaPago, bool CuentaPeca = false);
public sealed record ComprobacionAnticipoDatos(ImpuestosPedimento Impuestos, Guid PedimentoPagadoId, Guid ComprobantePagoId, DateOnly Fecha, IReadOnlyCollection<Guid>? GastoIds = null);
public sealed record ResolucionAnticipoDatos(string Tipo, decimal Importe, DateOnly Fecha, string Referencia, Guid EvidenciaId, Guid? AnticipoDestinoId = null, int? VersionDestino = null);
public sealed record ConceptoGastoDatos(BaseDistribucion Importacion, BaseDistribucion Exportacion, bool? RequiereOrdenServicio = null);
public sealed record FinanzasDetalle(int Version, string EstadoFinanciero, IReadOnlyList<string> Pendientes,
    IReadOnlyList<ConceptoGasto> Conceptos, IReadOnlyCollection<GastoExpediente> Gastos, IReadOnlyCollection<PartidaCosto> Bases,
    IReadOnlyList<BasePartidaCosto> Partidas, IReadOnlyCollection<DistribucionGasto> Distribuciones, IReadOnlyCollection<AnticipoAgente> Anticipos, IReadOnlyDictionary<Guid, string> Nombres)
{
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
    ComprobacionAnticipoDatos? Comprobacion = null, ResolucionAnticipoDatos? Resolucion = null, ConceptoGastoDatos? Concepto = null) : IRequest<FinanzasDetalle>;
public sealed record ExpedienteCostoDistribuidoEvent(Guid Empresa, DateTimeOffset Fecha, Guid ExpedienteId, Guid SucursalId,
    int NumeroVersion, IReadOnlyList<CostoPartidaDistribuido> Partidas)
    : IntegrationEvent("comercio_exterior.expediente.costo-distribuido.v1", Empresa, Fecha);

public sealed record AnticipoAgenteSaldoTransferidoEvent(Guid Empresa, DateTimeOffset OcurridoEn,
    Guid ExpedienteId, string FolioExpediente, Guid AnticipoAgenteId, int ResolucionIndice,
    Guid AgenteId, decimal ImporteMxn, DateOnly Fecha, string Referencia)
    : IntegrationEvent("comercio_exterior.anticipo-agente.saldo-transferido.v1", Empresa, OcurridoEn);

public sealed class FinanzasServicio(ComercioExteriorDbContext db, ExpedienteServicio servicio,
    IRecepcionesCostoReadPort recepciones, IClock clock, ICatalogosCeReadPort catalogos)
{
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
            var recibidas = await recepciones.ListarAsync(e.EmpresaId, e.SucursalId, e.OcId!.Value, ct);
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
        var usuarios = await catalogos.NombresAsync(e.Gastos.Where(g => g.TipoCambioModificadoPor is not null).Select(g => g.TipoCambioModificadoPor!.Value).Distinct(), ct);
        return new(e.Version, e.EstadoFinanciero,
        e.MotivosFinancieros(), await ConceptosAsync(e.EmpresaId, ct), e.Gastos, e.PartidasCosto, await PartidasAsync(e, ct), e.Distribuciones, e.Anticipos,
            terceros.Concat(usuarios).GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.First().Value));
    }
    public async Task<DistribucionGasto> PreviaAsync(Guid id, int version, CancellationToken ct)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorGastosDistribuir, ct);
        var e = await servicio.CargarAsync(id, true, ct);
        if (e.Version != version) throw new ConcurrencyException("Expediente", id);
        return e.PrepararDistribucion(await ConceptosAsync(e.EmpresaId, ct), await PartidasAsync(e, ct), servicio.Actor, clock.UtcNow);
    }
}

public sealed class MutarFinanzasValidator : AbstractValidator<MutarFinanzasCommand>
{
    public MutarFinanzasValidator()
    {
        RuleFor(x => x.ExpedienteId).NotEmpty(); RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Resolucion!.Tipo).Must(t => t is "compensar" or "devolucion" or "pago-complementario" or "saldo-al-agente")
            .When(x => x.Resolucion is not null);
        RuleFor(x => x.Accion).Must(x => x is "gasto" or "partida" or "distribuir" or "anticipo" or "comprobar" or "resolver" or "concepto" or "inicializar-conceptos");
    }
}
public sealed class MutarFinanzasHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, FinanzasServicio finanzas,
    ICatalogosCeReadPort catalogos, IFacturaGastoReadPort facturas, ITipoCambioGastoReadPort cambio, IClock clock,
    IIntegrationEventPublisher eventos, OrdenesServicioServicio ordenes) : IRequestHandler<MutarFinanzasCommand, FinanzasDetalle>
{
    public async Task<FinanzasDetalle> Handle(MutarFinanzasCommand request, CancellationToken cancellationToken)
    {
        var permiso = request.Accion switch
        {
            "anticipo" or "comprobar" or "resolver" => PermisosCanonicos.ComercioExteriorAnticiposGestionar,
            "distribuir" => PermisosCanonicos.ComercioExteriorGastosDistribuir,
            _ => PermisosCanonicos.ComercioExteriorGastosRegistrar
        };
        await servicio.PermisoAsync(permiso, cancellationToken);
        await using var tx = request.Accion == "gasto" ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        // El saldo compartido abarca expedientes distintos; el ETag de un expediente no lo protege.
        if (tx is not null) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7060601)", cancellationToken);
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
        switch (request.Accion)
        {
            case "inicializar-conceptos":
                if (!await db.ConceptosGasto.AnyAsync(c => c.EmpresaId == e.EmpresaId, cancellationToken)) db.ConceptosGasto.AddRange(ConceptoGasto.CrearCatalogo(e.EmpresaId));
                e.MarcarCambioFinanciero();
                break;
            case "concepto":
                var config = request.Concepto ?? throw new BusinessRuleException("CE_CONCEPTO_INVALIDO", "Capture las bases.");
                var conceptoEditar = await db.ConceptosGasto.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw new EntityNotFoundException("CE_CONCEPTO_INVALIDO", "No existe el concepto.");
                conceptoEditar.Configurar(config.Importacion, config.Exportacion, config.RequiereOrdenServicio);
                // El catálogo es de empresa: no se permite alterar expedientes de sucursales fuera del alcance del actor.
                var afectados = await db.Expedientes.Where(x => x.Gastos.Any(g => g.ConceptoId == id)).ToListAsync(cancellationToken);
                foreach (var afectado in afectados.Where(x => x.Estado == EstadoExpediente.Abierto))
                { await servicio.SucursalAsync(afectado.SucursalId, true, cancellationToken); afectado.MarcarCambioFinanciero(true); }
                if (!afectados.Any(x => x.Id == e.Id)) e.MarcarCambioFinanciero();
                break;
            case "gasto":
                var g = request.Gasto ?? throw new BusinessRuleException("CE_GASTO_INVALIDO", "Capture el gasto.");
                if (g.FechaFactura > clock.HoyLocal()) throw new BusinessRuleException("CE_FECHA_INVALIDA", "La fecha de factura no puede ser futura.");
                await catalogos.ValidarParticipanteAsync(g.ProveedorId, null, cancellationToken);
                var concepto = await db.ConceptosGasto.SingleOrDefaultAsync(c => c.Id == g.ConceptoId, cancellationToken) ?? throw new BusinessRuleException("CE_CONCEPTO_INVALIDO", "Seleccione un concepto del catálogo.");
                var factura = await facturas.BuscarAsync(g.ProveedorId, g.Factura, g.FacturaProveedorId, cancellationToken);
                if ((g.FacturaProveedorId is not null && factura is null) || (factura is not null && (factura.SucursalId != e.SucursalId || factura.Moneda != g.Moneda || factura.Fecha != g.FechaFactura)))
                    throw new BusinessRuleException("CE_FACTURA_INVALIDA", "La factura debe corresponder al proveedor, sucursal, moneda y fecha del gasto.");
                var sugerido = g.Moneda == "MXN" ? 1 : await cambio.ObtenerBanxicoAsync(g.FechaFactura, cancellationToken);
                var tc = sugerido; var fuenteTc = g.Moneda == "MXN" ? "MXN" : "Banxico";
                if (g.Moneda != "MXN" && g.FuenteTipoCambio is not null)
                {
                    fuenteTc = g.FuenteTipoCambio;
                    tc = fuenteTc == "Banxico" ? sugerido
                        : fuenteTc == "CFDI" && factura?.TipoCambio is decimal delCfdi ? delCfdi
                        : g.TipoCambioUsado ?? throw new BusinessRuleException("CE_TIPO_CAMBIO_REQUERIDO", "Capture el tipo de cambio usado.");
                }
                var orden = await ordenes.ValidarAsync(e, g, concepto, tc, cancellationToken);
                if (orden?.Moneda == "USD" && g.Moneda == "MXN")
                    orden = orden with { TipoCambio = await cambio.ObtenerBanxicoAsync(g.FechaFactura, cancellationToken) };
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
                var distribucion = e.PrepararDistribucion(await finanzas.ConceptosAsync(e.EmpresaId, cancellationToken), await finanzas.PartidasAsync(e, cancellationToken), servicio.Actor, clock.UtcNow);
                e.ConfirmarDistribucion(distribucion);
                if (e.Tipo == TipoExpediente.Importacion)
                    await eventos.PublishAsync(new ExpedienteCostoDistribuidoEvent(e.EmpresaId, clock.UtcNow, e.Id, e.SucursalId, distribucion.NumeroVersion, distribucion.Partidas), cancellationToken);
                break;
            case "anticipo":
                var a = request.Anticipo ?? throw new BusinessRuleException("CE_ANTICIPO_INVALIDO", "Capture el anticipo.");
                if (a.Fecha > clock.HoyLocal()) throw new BusinessRuleException("CE_FECHA_INVALIDA", "La fecha no puede ser futura.");
                await catalogos.ValidarParticipanteAsync(a.AgenteId, null, cancellationToken);
                e.RegistrarAnticipo(id, a.AgenteId, a.Pedimento, a.Impuestos ?? throw new BusinessRuleException("CE_IMPUESTOS_INVALIDOS", "Capture IVA, IGI y PRV."), a.Fecha, a.ReferenciaPago, a.CuentaPeca);
                break;
            case "comprobar":
                var c = request.Comprobacion ?? throw new BusinessRuleException("CE_COMPROBACION_INVALIDA", "Capture los impuestos pagados.");
                if (c.Fecha > clock.HoyLocal()) throw new BusinessRuleException("CE_FECHA_INVALIDA", "La fecha no puede ser futura.");
                await Evidencia(c.PedimentoPagadoId); await Evidencia(c.ComprobantePagoId);
                var gastoIds = c.GastoIds ?? [];
                var gastos = e.Gastos.Where(g => gastoIds.Contains(g.Id)).ToArray();
                if (gastos.Length != gastoIds.Count || gastoIds.Distinct().Count() != gastoIds.Count)
                    throw new BusinessRuleException("CE_GASTO_AGENTE_INVALIDO", "Seleccione gastos únicos del expediente.");
                Anticipo().Comprobar(c.Impuestos ?? throw new BusinessRuleException("CE_IMPUESTOS_INVALIDOS", "Capture los impuestos pagados."), c.PedimentoPagadoId, c.ComprobantePagoId, c.Fecha);
                Anticipo().AplicarGastos(gastos); e.MarcarCambioFinanciero();
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
