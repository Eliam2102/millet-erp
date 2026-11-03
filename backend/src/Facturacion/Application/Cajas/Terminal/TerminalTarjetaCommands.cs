using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Terminal;

public sealed record CargaTerminalResponse(
    Guid Id, string NombreArchivo, DateOnly FechaArchivo, DateTimeOffset CargadoEn, int Renglones, int Nuevos,
    int Duplicados, int Emparejados, int SinEmparejar, int Rechazados, int Liquidaciones);

/// <summary>
/// Carga el archivo diario de la terminal de tarjetas (86e3vr1tf, Parte A):
/// guarda cada venta una sola vez por número de operación (recargar no duplica,
/// criterio 2), la empareja con su cobro de caja por autorización, importe y día
/// (criterio 1) o la deja «sin emparejar» sin asiento (criterio 3), y forma la
/// liquidación por folio con su evento contable cuando el archivo trae comisión,
/// IVA, neto y folio.
/// </summary>
public sealed record CargarArchivoTerminalCommand(string NombreArchivo, byte[] Contenido, DateOnly FechaArchivo)
    : IRequest<CargaTerminalResponse>;

/// <summary>Vuelve a emparejar las ventas «sin emparejar» (p. ej. después de registrar el cobro).</summary>
public sealed record ReemparejarTerminalCommand : IRequest<CargaTerminalResponse>;

public sealed class TerminalTarjetaHandlers :
    IRequestHandler<CargarArchivoTerminalCommand, CargaTerminalResponse>,
    IRequestHandler<ReemparejarTerminalCommand, CargaTerminalResponse>
{
    /// <summary>c_FormaPago de tarjeta que liquida la terminal: 04 crédito, 28 débito.</summary>
    private static readonly string[] FormasTarjeta = ["04", "28"];

    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly IOptionsMonitor<TerminalTarjetaOptions> _opciones;

    public TerminalTarjetaHandlers(
        FacturacionDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext user, IClock clock,
        IIntegrationEventPublisher eventos, IOptionsMonitor<TerminalTarjetaOptions> opciones)
    {
        _db = db; _empresa = empresa; _user = user; _clock = clock; _eventos = eventos; _opciones = opciones;
    }

    public async Task<CargaTerminalResponse> Handle(CargarArchivoTerminalCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (empresaId, usuarioId) = ContextoRequerido.De(_empresa, _user);
        if (command.Contenido.Length == 0)
            throw new BusinessRuleException("TERMINAL_ARCHIVO_VACIO", "El archivo de la terminal está vacío.");
        var renglones = LectorArchivoTerminal.Leer(command.Contenido, command.FechaArchivo, _opciones.CurrentValue);

        var carga = CargaTerminal.Iniciar(empresaId, command.NombreArchivo, Convert.ToHexString(SHA256.HashData(command.Contenido)),
            command.FechaArchivo, usuarioId, _clock.UtcNow);
        _db.CargasTerminal.Add(carga);

        var operaciones = renglones.Select(r => r.NoOperacion.Trim()).ToList();
        var existentes = (await _db.TransaccionesTerminal.AsNoTracking()
                .Where(t => operaciones.Contains(t.NoOperacion)).Select(t => t.NoOperacion).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var nuevas = new List<TransaccionTerminal>();
        foreach (var r in renglones)
        {
            // Un renglón repetido en el mismo archivo tampoco se duplica.
            if (!existentes.Add(r.NoOperacion.Trim())) continue;
            var t = TransaccionTerminal.Registrar(empresaId, carga.Id, r);
            _db.TransaccionesTerminal.Add(t);
            nuevas.Add(t);
        }

        await EmparejarAsync(nuevas, ct);
        var liquidaciones = await LiquidarAsync(empresaId, carga.Id, ct);
        carga.Resumir(renglones.Count, nuevas.Count, renglones.Count - nuevas.Count,
            nuevas.Count(t => t.Estado == EstadoTransaccionTerminal.Emparejada),
            nuevas.Count(t => t.Estado == EstadoTransaccionTerminal.SinEmparejar),
            nuevas.Count(t => t.Estado == EstadoTransaccionTerminal.Rechazada),
            liquidaciones);
        await _db.SaveChangesAsync(ct);
        return Respuesta(carga);
    }

    public async Task<CargaTerminalResponse> Handle(ReemparejarTerminalCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (empresaId, usuarioId) = ContextoRequerido.De(_empresa, _user);
        var pendientes = await _db.TransaccionesTerminal
            .Where(t => t.Estado == EstadoTransaccionTerminal.SinEmparejar).ToListAsync(ct);
        var carga = CargaTerminal.Iniciar(empresaId, "Reintento de emparejamiento", "-",
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), usuarioId, _clock.UtcNow);
        _db.CargasTerminal.Add(carga);
        await EmparejarAsync(pendientes, ct);
        var liquidaciones = await LiquidarAsync(empresaId, carga.Id, ct);
        var emparejadas = pendientes.Count(t => t.Estado == EstadoTransaccionTerminal.Emparejada);
        carga.Resumir(pendientes.Count, 0, 0, emparejadas, pendientes.Count - emparejadas, 0, liquidaciones);
        await _db.SaveChangesAsync(ct);
        return Respuesta(carga);
    }

    /// <summary>
    /// Venta aprobada ↔ forma 04/28 de un cobro vigente con la misma autorización
    /// (referencia del voucher), el mismo importe y el mismo día de operación de
    /// la sesión de caja. Cada forma de pago se usa una sola vez.
    /// </summary>
    private async Task EmparejarAsync(IReadOnlyCollection<TransaccionTerminal> transacciones, CancellationToken ct)
    {
        var porEmparejar = transacciones
            .Where(t => t.Estado == EstadoTransaccionTerminal.SinEmparejar && t.Autorizacion is not null).ToList();
        if (porEmparejar.Count == 0) return;
        // El GMR trae COD_AUTHORIZATION como número: puede llegar sin ceros a la izquierda
        // («21747» por «021747»). Se busca también la versión con ceros hasta 6 dígitos.
        var autorizaciones = porEmparejar.Select(t => t.Autorizacion!)
            .SelectMany(a => a.Length < 6 && a.All(char.IsDigit) ? new[] { a, a.PadLeft(6, '0') } : [a])
            .Distinct().ToList();

        var candidatas = await (
            from fp in _db.Set<CobroMostradorFormaPago>().AsNoTracking()
            join c in _db.CobrosMostrador.AsNoTracking() on fp.CobroMostradorId equals c.Id
            join s in _db.CajaSesiones.AsNoTracking() on c.CajaSesionId equals s.Id
            where c.Estado == EstadoCobroMostrador.Registrado
                  && FormasTarjeta.Contains(fp.FormaPago)
                  && fp.Referencia != null && autorizaciones.Contains(fp.Referencia)
                  && !_db.TransaccionesTerminal.Any(t => t.CobroFormaPagoId == fp.Id)
            select new { FormaPagoId = fp.Id, CobroId = c.Id, c.SucursalId, fp.Referencia, fp.Importe, s.DiaOperacion })
            .ToListAsync(ct);

        var usadas = new HashSet<Guid>();
        foreach (var t in porEmparejar.OrderBy(t => t.FechaHora))
        {
            var dia = DateOnly.FromDateTime(t.FechaHora);
            var forma = candidatas.FirstOrDefault(c => !usadas.Contains(c.FormaPagoId)
                && MismaAutorizacion(c.Referencia!, t.Autorizacion!)
                && c.Importe == t.Importe && c.DiaOperacion == dia);
            if (forma is null) continue;
            usadas.Add(forma.FormaPagoId);
            t.Emparejar(forma.CobroId, forma.FormaPagoId, forma.SucursalId);
        }
    }

    /// <summary>
    /// Agrupa por folio de liquidación las ventas emparejadas que traen comisión,
    /// IVA y neto y aún no tienen liquidación; publica el evento una vez por folio.
    /// Si el folio ya existe (otra carga), no se vuelve a publicar.
    /// </summary>
    private async Task<int> LiquidarAsync(Guid empresaId, Guid cargaId, CancellationToken ct)
    {
        var listas = _db.TransaccionesTerminal.Local
            .Where(t => t.Estado == EstadoTransaccionTerminal.Emparejada && t.LiquidacionId is null && t.TraeLiquidacion)
            .ToList();
        var guardadas = await _db.TransaccionesTerminal
            .Where(t => t.Estado == EstadoTransaccionTerminal.Emparejada && t.LiquidacionId == null
                && t.Comision != null && t.IvaComision != null && t.Neto != null && t.FolioLiquidacion != null)
            .ToListAsync(ct);
        var todas = listas.Concat(guardadas).DistinctBy(t => t.Id).ToList();
        if (todas.Count == 0) return 0;

        var folios = todas.Select(t => t.FolioLiquidacion!).Distinct().ToList();
        var yaLiquidados = (await _db.LiquidacionesTerminal.AsNoTracking()
            .Where(l => folios.Contains(l.Folio)).Select(l => l.Folio).ToListAsync(ct)).ToHashSet();

        var creadas = 0;
        foreach (var grupo in todas.GroupBy(t => t.FolioLiquidacion!).Where(g => !yaLiquidados.Contains(g.Key)))
        {
            var ventas = grupo.ToList();
            // Fecha del cargo = día en que el banco liquidó (DAT_SETTLED del GMR); sin ella, el de la última venta.
            var liquidacion = LiquidacionTerminal.Crear(empresaId, cargaId, grupo.Key,
                ventas.Max(v => v.FechaLiquidacion ?? DateOnly.FromDateTime(v.FechaHora)), ventas);
            _db.LiquidacionesTerminal.Add(liquidacion);
            await _eventos.PublishAsync(new TerminalLiquidacionRegistradaIntegrationEvent(
                empresaId, _clock.UtcNow, liquidacion.Id, liquidacion.Folio, liquidacion.Fecha, liquidacion.Bruto,
                liquidacion.Comision, liquidacion.IvaComision, liquidacion.Neto,
                ventas.Select(v => new VentaTerminalLiquidada(v.CobroMostradorId!.Value, v.SucursalId!.Value, v.Importe, v.Autorizacion)).ToList()),
                ct);
            creadas++;
        }
        return creadas;
    }

    private static bool MismaAutorizacion(string voucher, string banco)
    {
        var v = voucher.Trim();
        var b = banco.Trim();
        if (string.Equals(v, b, StringComparison.OrdinalIgnoreCase)) return true;
        return v.All(char.IsDigit) && b.All(char.IsDigit) && v.TrimStart('0') == b.TrimStart('0');
    }

    private static CargaTerminalResponse Respuesta(CargaTerminal c) => new(
        c.Id, c.NombreArchivo, c.FechaArchivo, c.CargadoEn, c.Renglones, c.Nuevos, c.Duplicados, c.Emparejados,
        c.SinEmparejar, c.Rechazados, c.Liquidaciones);
}
