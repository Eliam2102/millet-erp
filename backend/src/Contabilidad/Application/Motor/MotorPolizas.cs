using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>Resultado del motor: la póliza (nueva o la misma por idempotencia) o el rechazo que quedó en la bandeja.</summary>
public sealed record ResultadoMotor(PolizaRegistrada? Poliza, Guid? RechazoId, CausaRechazoContable? Causa, string? Detalle, string? Mensaje)
{
    public bool Generada => Poliza is not null;
}

/// <summary>
/// Convierte un evento en póliza (plano C1 §3; ficha C1.4 §2): plantilla vigente a la fecha → roles a cuentas → partidas → puerto
/// (folio, autorización, cuadre, periodo, dimensiones e idempotencia ya los hace <see cref="IPolizaContablePort"/>, #111). Todo lo que
/// impide la póliza termina en <see cref="RechazoContable"/> con su causa; nada se pierde. Corre igual con o sin usuario
/// (C1.4-g): la empresa sale del evento.
/// </summary>
public sealed record GenerarPolizaDesdeEventoCommand(EventoContable Evento, string Usuario = PolizaContableMotor.Usuario) : IRequest<ResultadoMotor>;

public static class PolizaContableMotor
{
    public const string Usuario = "motor-contable";
    public const string RolPuente = "MERCANCIA_RECIBIDA_POR_FACTURAR";
}

/// <summary>Causa y detalle de por qué un evento no produjo póliza.</summary>
public sealed class RechazoMotorException(CausaRechazoContable causa, string? detalle, string mensaje) : Exception(mensaje)
{
    public CausaRechazoContable Causa { get; } = causa;
    public string? Detalle { get; } = detalle;
}

public sealed class GenerarPolizaDesdeEventoHandler(ContabilidadDbContext db, IPolizaContablePort puerto, IClock clock)
    : IRequestHandler<GenerarPolizaDesdeEventoCommand, ResultadoMotor>
{
    public async Task<ResultadoMotor> Handle(GenerarPolizaDesdeEventoCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var e = request.Evento;
        try
        {
            var solicitud = await ArmarAsync(e, ct);
            var poliza = await puerto.RegistrarAutomaticaAsync(solicitud, ct);
            var resuelto = await ResolverRechazoAsync(e, poliza, request.Usuario, ct);
            return new(poliza, resuelto, null, null, null);
        }
        catch (Exception ex) when (Clasificar(ex) is { } r)
        {
            db.ChangeTracker.Clear();
            var rechazo = await RegistrarRechazoAsync(e, r.Causa, r.Detalle, ex.Message, ct);
            return new(null, rechazo.Id, r.Causa, r.Detalle, rechazo.Mensaje);
        }
    }

    /// <summary>Plantilla vigente + roles → solicitud al puerto. Lanza <see cref="RechazoMotorException"/> con la causa.</summary>
    private async Task<SolicitudPolizaAutomatica> ArmarAsync(EventoContable e, CancellationToken ct)
    {
        var plantilla = await db.PlantillasContables.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == e.EmpresaId && p.TipoEvento == e.Tipo && p.VigenteDesde <= e.FechaContable)
            .OrderByDescending(p => p.NumeroVersion).FirstOrDefaultAsync(ct)
            ?? throw new RechazoMotorException(CausaRechazoContable.SinPlantilla, e.Tipo, $"No hay plantilla vigente para {e.Tipo} al {e.FechaContable:yyyy-MM-dd}.");

        if (e.SucursalId is not { } sucursal)
            throw Falta("SucursalId");
        if (e.Moneda != Poliza.MonedaNacional && e.TipoCambio is not > 0)
            throw Falta("TipoCambio");
        var tc = e.Moneda == Poliza.MonedaNacional ? 1m : e.TipoCambio!.Value;

        var codigos = plantilla.Lineas.Select(l => l.Rol).Distinct().ToList();
        var roles = await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.EmpresaId == e.EmpresaId && codigos.Contains(r.Codigo) && r.DeletedAt == null).ToListAsync(ct);

        var partidas = new List<PartidaRequest>();
        foreach (var l in plantilla.Lineas.OrderBy(x => x.Orden))
        {
            Guid? cuenta = null;
            (Guid? Proveedor, Guid? Cliente, OrigenMovimiento Origen) auxiliar = l.Auxiliar switch
            {
                AuxiliarPlantilla.Proveedor => (e.ProveedorId ?? throw Falta("ProveedorId"), null, OrigenMovimiento.AuxiliarCxP),
                AuxiliarPlantilla.Cliente => (null, e.ClienteId ?? throw Falta("ClienteId"), OrigenMovimiento.AuxiliarCxC),
                _ => (null, null, OrigenMovimiento.Automatica),
            };
            foreach (var (importe, centro, puente) in Importes(l, e))
            {
                if (importe == 0m) continue;
                // El rol se resuelve solo si la línea mueve dinero (GASTO sin servicios o retenciones en cero no exigen cuenta).
                cuenta ??= Cuenta(roles, l.Rol, sucursal, e.Moneda, e.FechaContable);
                var mxn = Math.Round(importe * tc, 2, MidpointRounding.AwayFromZero);
                var cargo = l.Lado == LadoPartida.Cargo ? mxn : 0m;
                var abono = l.Lado == LadoPartida.Abono ? mxn : 0m;
                if (mxn < 0) (cargo, abono) = (-abono, -cargo);
                partidas.Add(new PartidaRequest(cuenta.Value, l.Concepto, cargo, abono, sucursal,
                    Dim3Id: l.ConCentroCosto ? centro : null, ClienteId: auxiliar.Cliente, ProveedorId: auxiliar.Proveedor,
                    Moneda: e.Moneda, TipoCambio: tc, ImporteOriginal: e.Moneda == Poliza.MonedaNacional ? null : importe,
                    Origen: auxiliar.Origen, Puente: l.Rol == PolizaContableMotor.RolPuente ? puente : null));
            }
        }

        var cargos = partidas.Sum(p => p.Cargo);
        var abonos = partidas.Sum(p => p.Abono);
        if (partidas.Count < 2 || cargos != abonos)
            throw new RechazoMotorException(CausaRechazoContable.NoCuadra, $"{cargos:N2} vs {abonos:N2}",
                $"La póliza no cuadra en MXN: cargos {cargos:N2} y abonos {abonos:N2}.");

        return new SolicitudPolizaAutomatica(plantilla.TipoOperacionClave, e.FechaContable, plantilla.Concepto, e.Documento, e.Llave,
            partidas, OrigenMovimiento.Automatica, e.EmpresaId);
    }

    /// <summary>
    /// Importes de una línea de plantilla: uno solo (el campo con nombre) o, con <see cref="LineaPlantillaContable.PorLinea"/>, uno por
    /// cada línea del evento (E6: servicios a gasto por centro de costo; el resto al puente con su <see cref="ReferenciaPuente"/>).
    /// </summary>
    private static IEnumerable<(decimal Importe, Guid? Centro, ReferenciaPuente? Puente)> Importes(LineaPlantillaContable l, EventoContable e)
    {
        if (!l.PorLinea)
        {
            if (!e.Importes.TryGetValue(l.Campo, out var valor) || valor is null) throw Falta(l.Campo);
            yield return (valor.Value, e.CentroCostoId, Puente(e, null));
            yield break;
        }
        var lineas = e.Lineas.Where(x => l.SoloServicios is null || x.EsServicio == l.SoloServicios).ToList();
        // Evento sin desglose: toda la base va a una sola partida (si la plantilla pide no-servicios, que es el caso por omisión).
        if (e.Lineas.Count == 0 && l.SoloServicios != true)
        {
            if (!e.Importes.TryGetValue(l.Campo, out var valor) || valor is null) throw Falta(l.Campo);
            yield return (valor.Value, e.CentroCostoId, Puente(e, null));
            yield break;
        }
        foreach (var x in lineas)
            yield return (x.Importe, x.CentroCostoId ?? e.CentroCostoId, Puente(e, x.LineaOcId));
    }

    private static ReferenciaPuente? Puente(EventoContable e, Guid? lineaOcId) =>
        e.OrdenCompraId is { } oc ? new ReferenciaPuente(oc, lineaOcId, TipoDocumentoPuente.Factura, e.Documento.Id) : null;

    /// <summary>El rol más específico vigente (sucursal y moneda exactas antes que el genérico). Sin cuenta → RolSinCuenta.</summary>
    private static Guid Cuenta(List<RolCuenta> roles, string codigo, Guid sucursal, string moneda, DateOnly fecha)
    {
        var rol = roles.Where(r => r.Codigo == codigo && r.VigenteEn(fecha)
                && (r.SucursalId is null || r.SucursalId == sucursal) && (r.Moneda is null || r.Moneda == moneda))
            .OrderByDescending(r => (r.SucursalId is null ? 0 : 2) + (r.Moneda is null ? 0 : 1)).ThenByDescending(r => r.VigenteDesde)
            .FirstOrDefault();
        return rol?.CuentaId ?? throw new RechazoMotorException(CausaRechazoContable.RolSinCuenta, codigo,
            $"El rol {codigo} no tiene cuenta asignada: Contabilidad debe mapearlo en Roles de cuenta.");
    }

    private static RechazoMotorException Falta(string campo) =>
        new(CausaRechazoContable.DatoFaltante, campo, $"El evento no trae {campo}, que la plantilla necesita.");

    /// <summary>Traduce lo que lanza el puerto a una causa de la bandeja. Lo que no reconoce (error técnico) se propaga.</summary>
    internal static (CausaRechazoContable Causa, string? Detalle)? Clasificar(Exception ex) => ex switch
    {
        RechazoMotorException r => (r.Causa, r.Detalle),
        BusinessRuleException b when b.Code.StartsWith("CONTAB_PERIODO_", StringComparison.Ordinal) => (CausaRechazoContable.PeriodoCerrado, b.Code),
        BusinessRuleException b when b.Code.Contains("CUADR", StringComparison.Ordinal) => (CausaRechazoContable.NoCuadra, b.Code),
        ValidationException v when v.Errors.Any(x => x.Code.StartsWith("CONTAB_DIM_", StringComparison.Ordinal) && x.Code.Contains("FALTANTE", StringComparison.Ordinal))
            => (CausaRechazoContable.DatoFaltante, v.Errors.First(x => x.Code.Contains("FALTANTE", StringComparison.Ordinal)).Field),
        ValidationException v => (CausaRechazoContable.CuentaNoValida, v.Errors.Count > 0 ? v.Errors[0].Field : null),
        BusinessRuleException b => (CausaRechazoContable.CuentaNoValida, b.Code),
        _ => null,
    };

    private async Task<RechazoContable> RegistrarRechazoAsync(EventoContable e, CausaRechazoContable causa, string? detalle, string mensaje, CancellationToken ct)
    {
        var existente = await db.RechazosContables.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.EmpresaId == e.EmpresaId && r.TipoEvento == e.Tipo && r.EventoId == e.EventoId, ct);
        if (existente is null)
        {
            existente = RechazoContable.Crear(e.EmpresaId, e.Tipo, e.EventoId, e.Payload, causa, detalle, mensaje, clock.UtcNow);
            db.RechazosContables.Add(existente);
        }
        else if (existente.Estado == EstadoRechazoContable.Pendiente)
            existente.FallaDeNuevo(causa, detalle, mensaje, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return existente;
    }

    /// <summary>Si el evento tenía un rechazo pendiente, la póliza lo resuelve (C1.4-b: queda «Reprocesado»).</summary>
    private async Task<Guid?> ResolverRechazoAsync(EventoContable e, PolizaRegistrada poliza, string usuario, CancellationToken ct)
    {
        var rechazo = await db.RechazosContables.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.EmpresaId == e.EmpresaId && r.TipoEvento == e.Tipo && r.EventoId == e.EventoId
                && r.Estado == EstadoRechazoContable.Pendiente, ct);
        if (rechazo is null) return null;
        rechazo.MarcarReprocesado(poliza.Id, usuario, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return rechazo.Id;
    }
}

/// <summary>
/// Entrada del motor desde JSON (endpoint interno de pruebas y, en C1.5, los listeners): normaliza y genera. Un payload ilegible no
/// llega a la bandeja: se responde 400 (el evento se reenvía corregido).
/// </summary>
public sealed record ProcesarEventoContableCommand(string Tipo, Guid EventoId, string Payload, string Usuario = PolizaContableMotor.Usuario)
    : IRequest<ResultadoMotor>;

public sealed class ProcesarEventoContableHandler(IMediator mediator) : IRequestHandler<ProcesarEventoContableCommand, ResultadoMotor>
{
    public async Task<ResultadoMotor> Handle(ProcesarEventoContableCommand r, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        EventoContable evento;
        try { evento = NormalizadorEventos.Normalizar(r.Tipo, r.EventoId, r.Payload); }
        catch (EventoIlegibleException ex) { throw new BusinessRuleException("CONTAB_EVENTO_ILEGIBLE", ex.Message); }
        return await mediator.Send(new GenerarPolizaDesdeEventoCommand(evento, r.Usuario), ct);
    }
}
