using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.Ports;
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

public sealed class GenerarPolizaDesdeEventoHandler(ContabilidadDbContext db, IPolizaContablePort puerto, IClock clock,
    ICentroCostoContabilidadPort centros, IEnumerable<IEnriquecedorEventoContable> enriquecedores)
    : IRequestHandler<GenerarPolizaDesdeEventoCommand, ResultadoMotor>
{
    public async Task<ResultadoMotor> Handle(GenerarPolizaDesdeEventoCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var e = request.Evento;
        try
        {
            // C1.5: datos que el evento no trae y Contabilidad ya conoce (p. ej. sucursal y proveedor de la póliza de la factura).
            foreach (var enriquecedor in enriquecedores) e = await enriquecedor.EnriquecerAsync(e, ct);
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
        var porClasificador = plantilla.Lineas.Where(l => l.RolPorClasificador).Select(l => l.Rol + "_").Distinct().ToList();
        // C1.5: con rol por clasificador se leen todos los roles de la empresa (catálogo chico) y se filtra en memoria por prefijo.
        var roles = (await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.EmpresaId == e.EmpresaId && r.DeletedAt == null && (porClasificador.Count > 0 || codigos.Contains(r.Codigo)))
                .ToListAsync(ct))
            .Where(r => codigos.Contains(r.Codigo) || porClasificador.Exists(prefijo => r.Codigo.StartsWith(prefijo, StringComparison.Ordinal)))
            .ToList();
        var niveles = plantilla.Lineas.Any(l => l.ConCentroCosto) ? await NivelesAsync(e, ct) : new Dictionary<Guid, DimensionContable>();

        var partidas = new List<PartidaRequest>();
        foreach (var l in plantilla.Lineas.OrderBy(x => x.Orden))
        {
            Guid? cuenta = null;
            foreach (var (importe, centro, puente, linea) in Importes(l, e))
            {
                if (importe == 0m) continue;
                // C1.5: con desglose por línea, el cliente de la línea manda (liquidación de efectivo: un abono por cobro).
                (Guid? Proveedor, Guid? Cliente, OrigenMovimiento Origen) auxiliar = l.Auxiliar switch
                {
                    AuxiliarPlantilla.Proveedor => (e.ProveedorId ?? throw Falta("ProveedorId"), null, OrigenMovimiento.AuxiliarCxP),
                    AuxiliarPlantilla.Cliente => (null, linea?.ClienteId ?? e.ClienteId ?? throw Falta("ClienteId"), OrigenMovimiento.AuxiliarCxC),
                    _ => (null, null, OrigenMovimiento.Automatica),
                };
                // El rol se resuelve solo si la línea mueve dinero (GASTO sin servicios o retenciones en cero no exigen cuenta).
                // C1.5: una cuenta fija de la línea (V64) o el rol con sufijo del clasificador (VENTAS_<tipo>) si existe; si no, el rol base.
                var cuentaPartida = linea?.CuentaId
                    ?? (l.RolPorClasificador && linea?.Clasificador is { Length: > 0 } clasificador
                        && CuentaOpcional(roles, $"{l.Rol}_{Clasificador(clasificador)}", sucursal, e.Moneda, e.FechaContable) is { } porTipo
                            ? porTipo
                            : cuenta ??= Cuenta(roles, l.Rol, sucursal, e.Moneda, e.FechaContable));
                var mxn = Math.Round(importe * tc, 2, MidpointRounding.AwayFromZero);
                var cargo = l.Lado == LadoPartida.Cargo ? mxn : 0m;
                var abono = l.Lado == LadoPartida.Abono ? mxn : 0m;
                if (mxn < 0) (cargo, abono) = (-abono, -cargo);
                var (dim1, dim2, dim3) = l.ConCentroCosto ? Dimensiones(centro, niveles) : (null, null, null);
                partidas.Add(new PartidaRequest(cuentaPartida, l.Concepto, cargo, abono, sucursal,
                    Dim1Id: dim1, Dim2Id: dim2, Dim3Id: dim3, ClienteId: auxiliar.Cliente, ProveedorId: auxiliar.Proveedor,
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
    private static IEnumerable<(decimal Importe, Guid? Centro, ReferenciaPuente? Puente, LineaEventoContable? Linea)> Importes(
        LineaPlantillaContable l, EventoContable e)
    {
        if (!l.PorLinea)
        {
            if (!e.Importes.TryGetValue(l.Campo, out var valor) || valor is null) throw Falta(l.Campo);
            yield return (valor.Value, e.CentroCostoId, Puente(e, null), null);
            yield break;
        }
        var lineas = e.Lineas.Where(x => l.SoloServicios is null || x.EsServicio == l.SoloServicios).ToList();
        // Evento sin desglose: toda la base va a una sola partida (si la plantilla pide no-servicios, que es el caso por omisión).
        if (e.Lineas.Count == 0 && l.SoloServicios != true)
        {
            if (!e.Importes.TryGetValue(l.Campo, out var valor) || valor is null) throw Falta(l.Campo);
            yield return (valor.Value, e.CentroCostoId, Puente(e, null), null);
            yield break;
        }
        foreach (var x in lineas)
            yield return (x.Importe, x.CentroCostoId ?? e.CentroCostoId, Puente(e, x.LineaOcId), x);
    }

    /// <summary>Nivel de cada centro del evento (cabecera y líneas), leído de CentrosCosto en un solo viaje.</summary>
    private async Task<IReadOnlyDictionary<Guid, DimensionContable>> NivelesAsync(EventoContable e, CancellationToken ct)
    {
        var ids = e.Lineas.Select(x => x.CentroCostoId).Append(e.CentroCostoId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, DimensionContable>();
        var nodos = await centros.ObtenerAsync(ids, ct);
        return nodos.ToDictionary(n => n.Key, n => n.Value.Nivel);
    }

    /// <summary>
    /// ADM-08: el centro de la línea de OC puede ser de cualquier nivel (Dim1 ubicación, Dim2 CeCo, Dim3 equipo); va en la dimensión de
    /// su nivel y el validador de K10.2 deriva los superiores. Un centro que no está en el catálogo va en Dim3 para que el validador lo
    /// rechace como inexistente.
    /// </summary>
    private static (Guid? Dim1, Guid? Dim2, Guid? Dim3) Dimensiones(Guid? centro, IReadOnlyDictionary<Guid, DimensionContable> niveles)
    {
        if (centro is not { } id) return (null, null, null);
        return niveles.GetValueOrDefault(id) switch
        {
            DimensionContable.Dim1 => (id, null, null),
            DimensionContable.Dim2 => (null, id, null),
            _ => (null, null, id),
        };
    }

    /// <summary>
    /// C1.5: el tipo de documento del puente lo fija el evento (recepción, factura, devolución, nota de cargo o de crédito). Una NC o
    /// nota de cargo sin OC en el evento referencia la factura origen (<see cref="ReferenciaPuente.DocumentoOrigenFacturaId"/>) con OC
    /// vacía: K10.5 la resuelve por la factura.
    /// </summary>
    private static ReferenciaPuente? Puente(EventoContable e, Guid? lineaOcId) => e.OrdenCompraId is { } oc
        ? new ReferenciaPuente(oc, lineaOcId, e.TipoPuente, e.Documento.Id, e.FacturaOrigenId)
        : e.FacturaOrigenId is { } factura ? new ReferenciaPuente(Guid.Empty, lineaOcId, e.TipoPuente, e.Documento.Id, factura) : null;

    /// <summary>Normaliza el clasificador para el sufijo del rol: mayúsculas, dígitos y guion bajo.</summary>
    internal static string Clasificador(string valor) =>
        new([.. valor.Trim().ToUpperInvariant().Select(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) ? c : '_')]);

    /// <summary>El rol más específico vigente (sucursal y moneda exactas antes que el genérico). Sin cuenta → RolSinCuenta.</summary>
    private static Guid Cuenta(List<RolCuenta> roles, string codigo, Guid sucursal, string moneda, DateOnly fecha)
    {
        var rol = Rol(roles, codigo, sucursal, moneda, fecha);
        return rol?.CuentaId ?? throw new RechazoMotorException(CausaRechazoContable.RolSinCuenta, codigo,
            $"El rol {codigo} no tiene cuenta asignada: Contabilidad debe mapearlo en Roles de cuenta.");
    }

    /// <summary>C1.5: rol por clasificador (p. ej. VENTAS_PT) si existe y tiene cuenta; si no, el motor usa el rol base.</summary>
    private static Guid? CuentaOpcional(List<RolCuenta> roles, string codigo, Guid sucursal, string moneda, DateOnly fecha) =>
        Rol(roles, codigo, sucursal, moneda, fecha)?.CuentaId;

    private static RolCuenta? Rol(List<RolCuenta> roles, string codigo, Guid sucursal, string moneda, DateOnly fecha) =>
        roles.Where(r => r.Codigo == codigo && r.VigenteEn(fecha)
                && (r.SucursalId is null || r.SucursalId == sucursal) && (r.Moneda is null || r.Moneda == moneda))
            .OrderByDescending(r => (r.SucursalId is null ? 0 : 2) + (r.Moneda is null ? 0 : 1)).ThenByDescending(r => r.VigenteDesde)
            .FirstOrDefault();

    internal static RechazoMotorException Falta(string campo) =>
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

public sealed class ProcesarEventoContableHandler(ProcesadorEventosContables procesador) : IRequestHandler<ProcesarEventoContableCommand, ResultadoMotor>
{
    /// <summary>C1.5: el procesador enruta anulaciones, caja y consumo; el resto va al motor igual que en C1.4.</summary>
    public Task<ResultadoMotor> Handle(ProcesarEventoContableCommand r, CancellationToken cancellationToken) =>
        procesador.ProcesarAsync(r.Tipo, r.EventoId, r.Payload, r.Usuario, cancellationToken);
}
