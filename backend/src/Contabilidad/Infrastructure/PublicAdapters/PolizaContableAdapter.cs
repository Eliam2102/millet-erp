using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>Implementa <see cref="IPolizaContablePort"/> con las mismas piezas que las pólizas manuales.</summary>
public sealed class PolizaContableAdapter(ContabilidadDbContext db, RegistroPolizas registro, IClock clock,
    IEnumerable<IPolizaAutomaticaObservador> observadores) : IPolizaContablePort, IPolizaContableReadPort, IReversaPolizaContablePort
{
    public async Task<PolizaContableLectura?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Polizas.AsNoTracking().Include(p => p.Partidas).SingleOrDefaultAsync(p => p.Id == id, ct);
        return p == null ? null : new(p.Id, p.Estado, p.Partidas.OrderBy(x => x.Linea).Select(x => new PartidaRequest(x.CuentaId, x.Concepto, x.Cargo, x.Abono, x.SucursalId,
            Dim1Id:x.Dim1Id, Dim2Id:x.Dim2Id, Dim3Id:x.Dim3Id)).ToList());
    }
    public const string UsuarioMotor = "motor-contable";

    public async Task<PolizaRegistrada> RegistrarAutomaticaAsync(SolicitudPolizaAutomatica solicitud, CancellationToken ct)
    {
        var s = solicitud;
        if (await ExistenteAsync(s.LlaveIdempotencia, ct) is { } previa) return previa;
        Poliza? poliza = null;
        try
        {
            await registro.EnTransaccionAsync(async t =>
            {
                var clave = s.TipoClave.Trim().ToUpperInvariant();
                // Sin usuario (Bypass) el filtro de empresa no aplica: se acota a la empresa del evento.
                var tipo = await db.TiposDocumento.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Clave == clave && (s.EmpresaId == null || x.EmpresaId == s.EmpresaId), t)
                    ?? throw new BusinessRuleException("CONTAB_POLIZA_TIPO_NO_EXISTE", $"No existe el tipo de operación {clave}.");
                var (anio, numero) = RegistroPolizas.Periodo(s.Fecha, null);
                // C1.4-g: un listener corre sin usuario; la empresa viene del evento (mismo patrón que las recurrentes, K10.4).
                await registro.VerificarPeriodoAsync(anio, numero, OrigenPoliza.Automatica, t, s.EmpresaId);
                var partidas = await registro.ValidarPartidasAsync(s.Partidas, tipo.Id, s.Fecha, s.OrigenCuentas, t);
                var folio = await registro.ReservarNumeroAsync(tipo.Id, anio, t, s.EmpresaId);
                poliza = Poliza.CrearAutomatica(Guid.CreateVersion7(), tipo, s.Fecha, anio, numero, s.Concepto, s.Documento,
                    s.LlaveIdempotencia, partidas.Partidas, folio, clock.UtcNow);
                if (s.EmpresaId is { } empresaId)
                {
                    poliza.EmpresaId = empresaId;
                    foreach (var x in poliza.Partidas) x.EmpresaId = empresaId;
                }
                db.Polizas.Add(poliza);
                await db.SaveChangesAsync(t);
                await registro.RegistrarUsosAsync(poliza, partidas.ReglasAplicadas, t);
                await NotificarAsync(poliza, null, [.. s.Partidas.Select(x => x.Puente)], t);
                // Lo que agregue el observador (K10.5) se guarda en la misma transacción que la póliza.
                await db.SaveChangesAsync(t);
            }, ct);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_polizas_llave_idempotencia" })
        {
            db.ChangeTracker.Clear();
            return await ExistenteAsync(s.LlaveIdempotencia, ct) ?? throw new InvalidOperationException("Llave duplicada sin póliza.");
        }
        return new(poliza!.Id, poliza.Folio!, poliza.Estado, false);
    }

    public async Task<IReadOnlyList<PolizaRegistrada>> AnularPorDocumentoOrigenAsync(string modulo, string tipo, Guid documentoId, string motivo, CancellationToken ct)
    {
        var resultado = new List<PolizaRegistrada>();
        await registro.EnTransaccionAsync(async t =>
        {
            var polizas = await db.Polizas.Include(x => x.Partidas)
                .Where(x => x.OrigenModulo == modulo && x.OrigenTipo == tipo && x.OrigenDocumentoId == documentoId
                    && x.PolizaAnuladaId == null && x.Estado != EstadoPoliza.Borrador)
                .OrderBy(x => x.Fecha).ToListAsync(t);
            foreach (var p in polizas)
            {
                if (p.Estado == EstadoPoliza.Anulada)
                {
                    var previa = await db.Polizas.AsNoTracking().FirstAsync(x => x.Id == p.AnuladaPorPolizaId, t);
                    resultado.Add(new(previa.Id, previa.Folio!, previa.Estado, true));
                    continue;
                }
                // Fernando 10-nov (M-082): un mes cerrado no se reabre; si el periodo de la póliza ya no admite pólizas, la anulación
                // va en el periodo de hoy como póliza independiente (mismo criterio que RevertirAsync).
                var hoy = FechaContable.HoyLocal(clock.UtcNow);
                var cerrado = await registro.RechazoPeriodoAsync(p.PeriodoAnio, p.PeriodoNumero, p.Origen, t, p.EmpresaId);
                var anulacion = await registro.AnularAsync(p, motivo, cerrado == null ? null : (hoy.Year, hoy.Month), hoy, null, UsuarioMotor, clock.UtcNow, t);
                resultado.Add(new(anulacion.Id, anulacion.Folio!, anulacion.Estado, false));
                await NotificarAsync(anulacion, p.Id, [], t);
            }
            await db.SaveChangesAsync(t);
        }, ct);
        return resultado;
    }

    public async Task<ReversaPolizaContable> RevertirAsync(Guid polizaId, string motivo, DateOnly destino, Guid actor, CancellationToken ct)
    {
        Poliza? inversa = null;
        await registro.EnTransaccionAsync(async t =>
        {
            var original = await db.Polizas.Include(x => x.Partidas).SingleAsync(x => x.Id == polizaId, t);
            if (original.Estado != EstadoPoliza.Autorizada) throw new BusinessRuleException("CONTAB_REVERSA_INVALIDA", "La póliza de origen debe estar autorizada.");
            var cerrado = await registro.RechazoPeriodoAsync(original.PeriodoAnio, original.PeriodoNumero, original.Origen, t, original.EmpresaId);
            inversa = await registro.AnularAsync(original, motivo, cerrado == null ? null : (destino.Year, destino.Month), destino, actor, actor.ToString(), clock.UtcNow, t);
            await db.SaveChangesAsync(t); await NotificarAsync(inversa, original.Id, [], t); await db.SaveChangesAsync(t);
        }, ct);
        return new(inversa!.Id, inversa.Fecha);
    }

    /// <summary>C1.4 ↔ K10.5: avisa a los observadores dentro de la transacción (las partidas conservan su orden: línea = índice + 1).</summary>
    private async Task NotificarAsync(Poliza poliza, Guid? anuladaId, IReadOnlyList<ReferenciaPuente?> puentes, CancellationToken ct)
    {
        if (!observadores.Any()) return;
        var partidas = poliza.Partidas.OrderBy(x => x.Linea)
            .Select(x => new PartidaAutomaticaRegistrada(x.Linea, x.CuentaId, x.Cargo, x.Abono,
                x.Linea - 1 < puentes.Count ? puentes[x.Linea - 1] : null))
            .ToList();
        var aviso = new PolizaAutomaticaRegistrada(poliza.Id, poliza.Folio!, poliza.EmpresaId, poliza.Fecha,
            new DocumentoOrigenPoliza(poliza.OrigenModulo ?? string.Empty, poliza.OrigenTipo ?? string.Empty,
                poliza.OrigenDocumentoId ?? Guid.Empty, poliza.OrigenFolio, poliza.OrigenUuid),
            anuladaId, partidas);
        foreach (var o in observadores) await o.PolizaRegistradaAsync(aviso, ct);
    }

    private async Task<PolizaRegistrada?> ExistenteAsync(string llave, CancellationToken ct) =>
        await db.Polizas.AsNoTracking().Where(x => x.LlaveIdempotencia == llave)
            .Select(x => new { x.Id, x.TipoClave, x.Numero, x.Estado }).FirstOrDefaultAsync(ct) is { } p
            ? new(p.Id, Poliza.FormatearFolio(p.TipoClave, p.Numero!.Value), p.Estado, true)
            : null;
}
