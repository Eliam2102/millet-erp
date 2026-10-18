using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>Implementa <see cref="IPolizaContablePort"/> con las mismas piezas que las pólizas manuales.</summary>
public sealed class PolizaContableAdapter(ContabilidadDbContext db, RegistroPolizas registro, IClock clock) : IPolizaContablePort
{
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
                var tipo = await db.TiposDocumento.AsNoTracking().FirstOrDefaultAsync(x => x.Clave == clave, t)
                    ?? throw new BusinessRuleException("CONTAB_POLIZA_TIPO_NO_EXISTE", $"No existe el tipo de operación {clave}.");
                var (anio, numero) = RegistroPolizas.Periodo(s.Fecha, null);
                await registro.VerificarPeriodoAsync(anio, numero, OrigenPoliza.Automatica, t);
                var partidas = await registro.ValidarPartidasAsync(s.Partidas, tipo.Id, s.Fecha, s.OrigenCuentas, t);
                var folio = await registro.ReservarNumeroAsync(tipo.Id, anio, t);
                poliza = Poliza.CrearAutomatica(Guid.CreateVersion7(), tipo, s.Fecha, anio, numero, s.Concepto, s.Documento,
                    s.LlaveIdempotencia, partidas.Partidas, folio, clock.UtcNow);
                db.Polizas.Add(poliza);
                await db.SaveChangesAsync(t);
                await registro.RegistrarUsosAsync(poliza, partidas.ReglasAplicadas, t);
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
                var anulacion = await registro.AnularAsync(p, motivo, null, FechaContable.HoyLocal(clock.UtcNow), null, UsuarioMotor, clock.UtcNow, t);
                resultado.Add(new(anulacion.Id, anulacion.Folio!, anulacion.Estado, false));
            }
            await db.SaveChangesAsync(t);
        }, ct);
        return resultado;
    }

    private async Task<PolizaRegistrada?> ExistenteAsync(string llave, CancellationToken ct) =>
        await db.Polizas.AsNoTracking().Where(x => x.LlaveIdempotencia == llave)
            .Select(x => new { x.Id, x.TipoClave, x.Numero, x.Estado }).FirstOrDefaultAsync(ct) is { } p
            ? new(p.Id, Poliza.FormatearFolio(p.TipoClave, p.Numero!.Value), p.Estado, true)
            : null;
}
