using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Application.Polizas;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Application;
public sealed record SolicitudOperacion(string Tipo, DateOnly Fecha, decimal Importe, string Motivo, string? Evidencia, string? SolicitadoPor = null, Guid? RevierteA = null);
public sealed class OperacionesLibroServicio(ActivosFijosDbContext db, ActivoServicio acceso, LibroServicio libro,
    ITransaccionPolizaActivo transaccion, Millet.Facturacion.Domain.Ports.IDisposicionVentaReadPort ventas,
    IReversaPolizaContablePort reversas, Millet.SharedKernel.Application.IClock clock)
{
    public async Task<OperacionLibro> RegistrarAsync(Guid activoId, int version, SolicitudOperacion datos, CancellationToken ct)
    {
        await acceso.PermisoAsync(Permiso(datos.Tipo, false), ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await libro.BloquearAsync(ct);
        if (datos.Motivo?.Length > 2000 || datos.Tipo is not ("Baja" or "Amortizacion" or "Revalorizacion" or "ReversaCapitalizacion" or "ReversaVenta" or "ReversaBaja") || string.IsNullOrWhiteSpace(datos.Motivo)
            || datos.Fecha == default || (datos.Tipo is "Amortizacion" or "Revalorizacion" && (datos.Importe == 0 || decimal.Round(datos.Importe, 2) != datos.Importe))
            || (datos.Tipo == "Baja" && string.IsNullOrWhiteSpace(datos.Evidencia)))
            throw new BusinessRuleException("AF_OPERACION_INVALIDA", "Capture tipo, fecha, importe, motivo y adjunto para bajas.");
        if (datos.Tipo == "Baja") EvidenciaBaja.Validar(datos.Evidencia);
        var a = await acceso.CargarAsync(activoId, true, ct);
        if (a.Version != version) throw new ConcurrencyException("Activo", a.Id);
        if (datos.Tipo is not ("ReversaVenta" or "ReversaBaja") && a.Estado != EstadoActivo.Activo) throw new BusinessRuleException("AF_ACTIVO_NO_VIGENTE", "El activo no está vigente.");
        if (datos.Tipo == "Baja")
        {
            var pendiente = await db.Set<OperacionLibro>().Where(x => x.ActivoId == a.Id && x.Tipo == "Baja" && x.PolizaId == null && !x.Anulada).Select(x => (DateOnly?)x.Fecha).FirstOrDefaultAsync(ct);
            if (pendiente is DateOnly fecha) throw BajaPendiente(fecha);
            if (await ventas.TieneAbiertaAsync(a.Numero, ct))
                throw new BusinessRuleException("AF_VENTA_AUTORIZADA_ABIERTA", "Tiene una autorización de venta vigente");
        }
        if (datos.Tipo.StartsWith("Reversa", StringComparison.Ordinal))
        {
            if (await db.Set<OperacionLibro>().AnyAsync(x => x.ActivoId == a.Id && x.Tipo.StartsWith("Reversa") && x.PolizaId == null && !x.Anulada, ct))
                throw new BusinessRuleException("AF_REVERSA_PENDIENTE", "Ya hay una reversa pendiente de autorización.");
            await ValidarReversaAsync(a, datos.Tipo, datos.RevierteA, ct);
            if (datos.Tipo == "ReversaCapitalizacion" && a.CapitalizadoPor != acceso.Actor)
                throw new BusinessRuleException("AF_CAPITALIZADOR_REQUERIDO", "La reversa debe solicitarla quien capitalizó el activo.");
        }
        var config = await db.Set<ConfiguracionLibro>().SingleOrDefaultAsync(ct);
        if (datos.Tipo == "Revalorizacion" && config?.RevalorizacionHabilitada != true)
            throw new BusinessRuleException("AF_REVALORIZACION_APAGADA", "La revalorización está deshabilitada.");
        var op = new OperacionLibro { EmpresaId = a.EmpresaId, ActivoId = a.Id, Tipo = datos.Tipo, Fecha = datos.Fecha,
            Importe = datos.Importe, Motivo = datos.Motivo, Evidencia = datos.Evidencia, SolicitadoPor = datos.SolicitadoPor?.Trim() ?? "", RegistradoPor = acceso.Actor, RevierteA = datos.RevierteA };
        if (op.Tipo == "Baja") op.ValidarSolicitudBaja();
        db.Add(op);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505", ConstraintName: "ux_operacion_baja_pendiente" })
        {
            await tx.RollbackAsync(ct);
            var fecha = await db.Set<OperacionLibro>().Where(x => x.ActivoId == a.Id && x.Tipo == "Baja" && x.PolizaId == null && !x.Anulada).Select(x => x.Fecha).FirstAsync(ct);
            throw BajaPendiente(fecha);
        }
        await tx.CommitAsync(ct); return op;
    }
    public async Task<OperacionLibro> AutorizarAsync(Guid id, int version, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await libro.BloquearAsync(ct);
        await using var enlace = await transaccion.VincularAsync(ct);
        var op = await db.Set<OperacionLibro>().SingleAsync(x => x.Id == id, ct);
        await acceso.PermisoAsync(Permiso(op.Tipo, true), ct);
        var a = await acceso.CargarAsync(op.ActivoId, true, ct);
        if (op.Version != version) throw new ConcurrencyException("OperacionLibro", op.Id);
        if (op.Anulada) throw new BusinessRuleException("AF_SOLICITUD_CERRADA", "La solicitud ya está cerrada.");
        if (op.PolizaId != null) return op;
        var cfg = await db.Set<ConfiguracionLibro>().SingleOrDefaultAsync(ct) ?? new ConfiguracionLibro();
        op.Validar(acceso.Actor, cfg.RevalorizacionHabilitada);
        if (op.Tipo.StartsWith("Reversa", StringComparison.Ordinal))
        {
            var origen = await ValidarReversaAsync(a, op.Tipo, op.RevierteA, ct);
            if (origen != null && op.Fecha < origen.Fecha) throw new BusinessRuleException("AF_FECHA_INVALIDA", "La reversa no puede tener fecha anterior a la disposición.");
            await libro.PeriodoAsync(op.Fecha, ct); await libro.FechaModificableAsync(op.Fecha, ct);
            var polizaId = origen?.PolizaId ?? a.PolizaAltaId;
            if (polizaId == null) throw new BusinessRuleException("AF_POLIZA_ORIGEN_INVALIDA", "No hay póliza de origen para revertir.");
            var inversa = await reversas.RevertirAsync(polizaId.Value, op.Motivo, op.Fecha, acceso.Actor, ct);
            op.PolizaId = inversa.Id; op.Fecha = inversa.Fecha;
            if (op.Tipo == "ReversaCapitalizacion") a.RevertirCapitalizacion(false, false);
            else { a.Reintegrar(); origen!.Anulada = true; }
            op.AutorizadoPor = acceso.Actor;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return op;
        }
        if (op.Fecha < a.InicioUso || op.Fecha <= a.CorteCarga) throw new BusinessRuleException("AF_FECHA_INVALIDA", "La fecha debe ser posterior al corte y al inicio de uso.");
        await libro.FechaModificableAsync(op.Fecha, ct); await libro.PeriodoAsync(op.Fecha, ct);
        var ultima = await db.Set<CorridaDepreciacion>().Where(x => !x.Anulada).MaxAsync(x => (DateOnly?)x.Periodo, ct);
        var siguiente = ultima?.AddMonths(1) ?? CalculoDepreciacion.PrimerMes(a, cfg.DepreciarMismoMes);
        if (siguiente < CalculoDepreciacion.Mes(op.Fecha))
            throw new BusinessRuleException("AF_MESES_PENDIENTES", "Complete la depreciación hasta el mes anterior antes de autorizar este ajuste o baja.");
        if (op.Tipo == "Baja")
        {
            if (await ventas.TieneAbiertaAsync(a.Numero, ct)) throw new BusinessRuleException("AF_VENTA_PENDIENTE", "Tiene una autorización de venta vigente");
            await BajaAsync(a, op, cfg, 0, ct);
        }
        else
        {
            var clase = (await acceso.ClaseAsync(a.ClaseId, ct)).EnMes(op.Fecha);
            var monto = Math.Abs(op.Importe); var positivo = op.Importe > 0;
            var cuenta = op.Tipo == "Amortizacion" ? clase.CuentaDepreciacionId : clase.CuentaActivoId;
            var contra = op.Tipo == "Amortizacion" ? clase.CuentaGastoId : cfg.CuentaRevalorizacionId;
            var cargo = op.Tipo == "Amortizacion" ? !positivo : positivo;
            var partidas = new[] {
                await libro.PartidaAsync(cuenta, cargo ? monto : 0, cargo ? 0 : monto, a.SucursalId, a.EquipoId ?? a.CentroCostoId, op.Motivo, ct),
                await libro.PartidaAsync(contra, cargo ? 0 : monto, cargo ? monto : 0, a.SucursalId, a.EquipoId ?? a.CentroCostoId, op.Motivo, ct) };
            if (op.Tipo == "Amortizacion") a.Depreciar(op.Importe); else a.Revalorizar(op.Importe);
            op.PolizaId = await libro.RegistrarAsync(op.Id, op.Tipo, op.Fecha, $"AF-AJU-{op.Id}", partidas, ct);
        }
        op.AutorizadoPor = acceso.Actor;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return op;
    }
    public async Task BajaAsync(Activo a, OperacionLibro op, ConfiguracionLibro cfg, decimal precio, CancellationToken ct)
    {
        var clase = (await db.Clases.Include(c => c.Versiones).SingleAsync(x => x.Id == a.ClaseId, ct)).EnMes(op.Fecha);
        var partidas = new List<PartidaRequest>();
        async Task Agregar(Guid? cuenta, decimal cargo, decimal abono)
        { if (cargo != 0 || abono != 0) partidas.Add(await libro.PartidaAsync(cuenta, cargo, abono, a.SucursalId, a.EquipoId ?? a.CentroCostoId, op.Motivo, ct)); }
        await Agregar(clase.CuentaActivoId, 0, a.ValorActual);
        await Agregar(clase.CuentaDepreciacionId, a.DepreciacionAcumulada, 0);
        await Agregar(cfg.CuentaVentaPuenteId, precio, 0);
        var resultado = precio - a.ValorEnLibros;
        if (resultado > 0) await Agregar(cfg.CuentaUtilidadId, 0, resultado);
        else if (resultado < 0) await Agregar(cfg.CuentaPerdidaId, -resultado, 0);
        a.DarDeBaja(op.Fecha);
        op.Importe = precio;
        op.PolizaId = await libro.RegistrarAsync(op.Id, op.Tipo, op.Fecha, $"AF-BAJA-{op.Id}", partidas, ct);
    }
    public async Task CancelarVentaAsync(OperacionLibro op, CancellationToken ct)
    {
        if (op.Anulada || op.VentaCancelada) return;
        (await db.Activos.SingleAsync(x => x.Id == op.ActivoId, ct)).MarcarVentaCancelada();
        op.VentaCancelada = true;
    }
    public static string Permiso(string tipo, bool autorizar) => tipo?.StartsWith("Reversa", StringComparison.Ordinal) == true
        ? autorizar ? "activos_fijos.reversas.autorizar" : "activos_fijos.reversas.solicitar"
        : tipo == "Baja" ? autorizar ? "activos_fijos.bajas.autorizar" : "activos_fijos.bajas.registrar"
        : autorizar ? "activos_fijos.ajustes.autorizar" : "activos_fijos.ajustes.registrar";
    private async Task<OperacionLibro?> ValidarReversaAsync(Activo a, string tipo, Guid? originalId, CancellationToken ct)
    {
        if (tipo == "ReversaCapitalizacion")
        {
            var depreciada = await db.Set<CorridaDepreciacion>().AnyAsync(x => !x.Anulada && x.Detalles.Any(d => d.ActivoId == a.Id), ct);
            var dependencias = await db.Activos.AnyAsync(x => x.ActivoMejoradoId == a.Id, ct)
                || await db.Set<OperacionLibro>().AnyAsync(x => x.ActivoId == a.Id && !x.Anulada && (x.Tipo == "Baja" || x.Tipo == "Venta" || x.PolizaId != null), ct)
                || await ventas.TieneAbiertaAsync(a.Numero, ct);
            // Valida en una copia de los mismos invariantes sin cambiar el estado antes de autorizar.
            a.ValidarReversaCapitalizacion(depreciada, dependencias); return null;
        }
        var origen = await db.Set<OperacionLibro>().SingleOrDefaultAsync(x => x.Id == originalId && x.ActivoId == a.Id, ct);
        if (origen == null || origen.Anulada || origen.PolizaId == null
            || tipo == "ReversaVenta" && (origen.Tipo != "Venta" || !origen.VentaCancelada || a.Estado != EstadoActivo.VentaCanceladaPendienteReversa)
            || tipo == "ReversaBaja" && (origen.Tipo != "Baja" || a.Estado != EstadoActivo.DadoDeBaja))
            throw new BusinessRuleException("AF_REVERSA_INVALIDA", "Seleccione la baja contabilizada o venta cancelada pendiente de reversa.");
        return origen;
    }
    private static BusinessRuleException BajaPendiente(DateOnly fecha) => new("AF_BAJA_PENDIENTE_EXISTENTE",
        $"Ya hay una baja pendiente registrada el {fecha.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.GetCultureInfo("es-MX"))}");
    public async Task<OperacionLibro> RetirarAsync(Guid id, int version, string motivo, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await libro.BloquearAsync(ct);
        var op = await db.Set<OperacionLibro>().SingleAsync(x => x.Id == id, ct);
        await acceso.PermisoAsync(Permiso(op.Tipo, false), ct); await acceso.CargarAsync(op.ActivoId, true, ct);
        if (op.Version != version) throw new ConcurrencyException("OperacionLibro", op.Id);
        op.Retirar(acceso.Actor, motivo, clock.UtcNow);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return op;
    }
    public async Task<OperacionLibro> RechazarAsync(Guid id, int version, string motivo, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await libro.BloquearAsync(ct);
        var op = await db.Set<OperacionLibro>().SingleAsync(x => x.Id == id, ct);
        await acceso.PermisoAsync(Permiso(op.Tipo, true), ct); await acceso.CargarAsync(op.ActivoId, true, ct);
        if (op.Version != version) throw new ConcurrencyException("OperacionLibro", op.Id);
        op.Rechazar(acceso.Actor, motivo, clock.UtcNow);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return op;
    }
}
