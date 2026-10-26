using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Application.Polizas;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Application;
public sealed record SolicitudOperacion(string Tipo, DateOnly Fecha, decimal Importe, string Motivo, string? Evidencia);
public sealed class OperacionesLibroServicio(ActivosFijosDbContext db, ActivoServicio acceso, LibroServicio libro,
    ITransaccionPolizaActivo transaccion, IPolizaContablePort polizas)
{
    public async Task<OperacionLibro> RegistrarAsync(Guid activoId, int version, SolicitudOperacion datos, CancellationToken ct)
    {
        await acceso.PermisoAsync(datos.Tipo == "Baja" ? "activos_fijos.bajas.registrar" : "activos_fijos.ajustes.registrar", ct);
        if (datos.Tipo is not ("Baja" or "Amortizacion" or "Revalorizacion") || string.IsNullOrWhiteSpace(datos.Motivo)
            || datos.Fecha == default || (datos.Tipo != "Baja" && (datos.Importe == 0 || decimal.Round(datos.Importe, 2) != datos.Importe))
            || (datos.Tipo == "Baja" && string.IsNullOrWhiteSpace(datos.Evidencia)))
            throw new BusinessRuleException("AF_OPERACION_INVALIDA", "Capture tipo, fecha, importe, motivo y adjunto para bajas.");
        if (datos.Tipo == "Baja") EvidenciaBaja.Validar(datos.Evidencia);
        var a = await acceso.CargarAsync(activoId, true, ct);
        if (a.Version != version) throw new ConcurrencyException("Activo", a.Id);
        if (a.Estado != EstadoActivo.Activo) throw new BusinessRuleException("AF_ACTIVO_NO_VIGENTE", "El activo no está vigente.");
        var config = await db.Set<ConfiguracionLibro>().SingleOrDefaultAsync(ct);
        if (datos.Tipo == "Revalorizacion" && config?.RevalorizacionHabilitada != true)
            throw new BusinessRuleException("AF_REVALORIZACION_APAGADA", "La revalorización está deshabilitada.");
        var op = new OperacionLibro { EmpresaId = a.EmpresaId, ActivoId = a.Id, Tipo = datos.Tipo, Fecha = datos.Fecha,
            Importe = datos.Importe, Motivo = datos.Motivo, Evidencia = datos.Evidencia, RegistradoPor = acceso.Actor };
        db.Add(op); await db.SaveChangesAsync(ct); return op;
    }
    public async Task<OperacionLibro> AutorizarAsync(Guid id, int version, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await libro.BloquearAsync(ct);
        await using var enlace = await transaccion.VincularAsync(ct);
        var op = await db.Set<OperacionLibro>().SingleAsync(x => x.Id == id, ct);
        await acceso.PermisoAsync(op.Tipo == "Baja" ? "activos_fijos.bajas.autorizar" : "activos_fijos.ajustes.autorizar", ct);
        var a = await acceso.CargarAsync(op.ActivoId, true, ct);
        if (op.Version != version) throw new ConcurrencyException("OperacionLibro", op.Id);
        if (op.PolizaId != null) return op;
        var cfg = await db.Set<ConfiguracionLibro>().SingleOrDefaultAsync(ct) ?? new ConfiguracionLibro();
        op.Validar(acceso.Actor, cfg.RevalorizacionHabilitada);
        if (op.Fecha < a.InicioUso || op.Fecha <= a.CorteCarga) throw new BusinessRuleException("AF_FECHA_INVALIDA", "La fecha debe ser posterior al corte y al inicio de uso.");
        await libro.FechaModificableAsync(op.Fecha, ct); await libro.PeriodoAsync(op.Fecha, ct);
        var ultima = await db.Set<CorridaDepreciacion>().Where(x => !x.Anulada).MaxAsync(x => (DateOnly?)x.Periodo, ct);
        var siguiente = ultima?.AddMonths(1) ?? CalculoDepreciacion.PrimerMes(a, cfg.DepreciarMismoMes);
        if (siguiente < CalculoDepreciacion.Mes(op.Fecha))
            throw new BusinessRuleException("AF_MESES_PENDIENTES", "Complete la depreciación hasta el mes anterior antes de autorizar este ajuste o baja.");
        if (op.Tipo == "Baja") await BajaAsync(a, op, cfg, 0, ct);
        else
        {
            var clase = await acceso.ClaseAsync(a.ClaseId, ct);
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
        var clase = await db.Clases.SingleAsync(x => x.Id == a.ClaseId, ct);
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
        if (op.Anulada) return;
        await polizas.AnularPorDocumentoOrigenAsync("ActivosFijos", "Venta", op.Id, "Cancelación de factura de venta", ct);
        (await db.Activos.SingleAsync(x => x.Id == op.ActivoId, ct)).Reintegrar(); op.Anulada = true;
    }
}
