using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Contabilidad.Application.PublicPorts;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Application;
public sealed record FilaLibro(Guid ActivoId, string Numero, Guid ClaseId, string Clase, Guid SucursalId, Guid CentroCostoId, Guid? EquipoId, decimal Valor, decimal Acumulada, decimal Neto, string? Sucursal, string? CentroCosto, string? Equipo, bool TasaPropia = false);
public sealed record FilaConciliacion(Guid ClaseId, string Clase, decimal ValorLibro, decimal AcumuladaLibro, decimal? SaldoActivo, decimal? SaldoDepreciacion,
    decimal? DiferenciaValor, decimal? DiferenciaAcumulada, string? Observacion)
{ public decimal NetoLibro => ValorLibro - AcumuladaLibro; }
public sealed record ReporteConciliacion(IReadOnlyList<FilaConciliacion> Clases, IReadOnlyList<FilaLibro> Activos, IReadOnlyList<CorridaDepreciacion> Corridas, IReadOnlyList<OperacionLibro> Ajustes, IReadOnlyList<ConciliacionActivo>? DepreciacionPorActivo = null);
public sealed class ReportesLibroServicio(ActivosFijosDbContext db, ActivoServicio acceso, ISaldosContablesReadPort saldos, ICatalogosActivoReadPort catalogos, IPolizaContableReadPort lecturaPolizas, LibroServicio servicioLibro, IClock clock, ITransaccionPolizaActivo transaccion)
{
    public async Task<IReadOnlyList<FilaLibro>> LibroAsync(DateOnly corte, Guid? sucursal, CancellationToken ct)
    {
        var ids = await acceso.SucursalesAsync(sucursal, ct);
        var activos = await db.Activos.AsNoTracking().Include(x => x.Historial).Include(x => x.Traslados).AsSplitQuery()
            .Where(x => x.Estado != EstadoActivo.PorCapitalizar && x.InicioUso <= corte && (x.CorteCarga == null || x.CorteCarga <= corte)).ToListAsync(ct);
        var clases = await db.Clases.AsNoTracking().Include(x => x.Versiones).ToDictionaryAsync(x => x.Id, ct);
        var corridas = await db.Set<CorridaDepreciacion>().AsNoTracking().Include(x => x.Detalles).Where(x => !x.Anulada && x.Periodo <= corte).ToListAsync(ct);
        var ops = await db.Set<OperacionLibro>().AsNoTracking().Where(x => x.PolizaId != null && x.Fecha <= corte && (!x.Anulada || db.Set<OperacionLibro>().Any(r => r.RevierteA == x.Id && r.PolizaId != null && r.Fecha > corte))).ToListAsync(ct);
        var resultado = new List<FilaLibro>();
        foreach (var a in activos.Where(a => !ops.Any(o => o.ActivoId == a.Id && (o.Tipo == "Baja" || o.Tipo == "Venta"))))
        {
            var custodia = CalculoDepreciacion.CustodiaAFecha(a, corte);
            if (ids != null && !ids.Contains(custodia.SucursalId)) continue;
            var ajustes = ops.Where(o => o.ActivoId == a.Id);
            var valor = a.ValorOriginal + a.Historial.Where(h => h.Fecha <= corte).Sum(h => h.Importe) + ajustes.Where(o => o.Tipo == "Revalorizacion").Sum(o => o.Importe);
            var acumulada = a.AcumuladaCarga + corridas.Where(c => CalculoDepreciacion.Cierre(c.Periodo) <= corte).SelectMany(c => c.Detalles).Where(d => d.ActivoId == a.Id).Sum(d => d.Importe)
                + ajustes.Where(o => o.Tipo == "Amortizacion").Sum(o => o.Importe);
            var descripcion = await catalogos.DescribirAsync(custodia, ct);
            resultado.Add(new(a.Id, a.Numero, a.ClaseId, clases[a.ClaseId].Clave, custodia.SucursalId, custodia.CentroCostoId, custodia.EquipoId, valor, acumulada, valor - acumulada, descripcion.Sucursal, descripcion.CentroCosto, descripcion.Equipo, a.TasaContableAnual != null || a.TasaFiscalAnual != null));
        }
        return resultado;
    }
    public async Task<ReporteConciliacion> ConciliarAsync(DateOnly periodo, Guid? sucursal, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.conciliacion.ver", ct);
        var corte = CalculoDepreciacion.Cierre(periodo); var inicio = CalculoDepreciacion.Mes(periodo);
        var ids = await acceso.SucursalesAsync(sucursal, ct);
        var libro = await LibroAsync(corte, sucursal, ct);
        var clases = (await db.Clases.AsNoTracking().Include(x => x.Versiones).ToListAsync(ct)).Select(c => c.EnMes(periodo)).ToList();
        var cuentas = clases.SelectMany(c => new[] { c.CuentaActivoId, c.CuentaDepreciacionId }).OfType<Guid>().Distinct().ToList();
        var saldo = await saldos.ObtenerAsync(corte, cuentas, ids, ct);
        var filas = clases.Select(c =>
        {
            var valor = libro.Where(a => a.ClaseId == c.Id).Sum(a => a.Valor); var acumulada = libro.Where(a => a.ClaseId == c.Id).Sum(a => a.Acumulada);
            var compartida = clases.Any(otra => otra.Id != c.Id && ((c.CuentaActivoId != null && otra.CuentaActivoId == c.CuentaActivoId) || (c.CuentaDepreciacionId != null && otra.CuentaDepreciacionId == c.CuentaDepreciacionId)));
            decimal? activo = c.CuentaActivoId is Guid ca && !compartida ? saldo.GetValueOrDefault(ca) : null;
            decimal? dep = c.CuentaDepreciacionId is Guid cd && !compartida ? -saldo.GetValueOrDefault(cd) : null;
            return new FilaConciliacion(c.Id, c.Clave, valor, acumulada, activo, dep, valor - activo, acumulada - dep,
                compartida ? "Cuenta compartida entre clases: no es posible atribuir el saldo por clase." : activo == null || dep == null ? "Configure las cuentas de la clase." : null);
        }).Where(f => libro.Any(a => a.ClaseId == f.ClaseId) || (f.SaldoActivo != null && f.SaldoActivo != 0) || (f.SaldoDepreciacion != null && f.SaldoDepreciacion != 0)).ToList();
        var corridas = await db.Set<CorridaDepreciacion>().AsNoTracking().Include(c => c.Detalles.Where(d => ids == null || ids.Contains(d.SucursalId))).Where(c => c.Periodo == inicio).ToListAsync(ct);
        var operaciones = await (from o in db.Set<OperacionLibro>().AsNoTracking() join a in db.Activos on o.ActivoId equals a.Id
            where o.Fecha >= inicio && o.Fecha <= corte && (ids == null || ids.Contains(a.SucursalId)) select o).ToListAsync(ct);
        var porActivo = new List<ConciliacionActivo>();
        foreach (var corrida in corridas.Where(c => !c.Anulada))
        {
            var poliza = corrida.PolizaId is Guid pid ? await lecturaPolizas.ObtenerAsync(pid,ct) : null;
            foreach (var grupo in corrida.Detalles.GroupBy(d => new { d.ClaseId,d.SucursalId,d.CentroCostoId,d.EquipoId }))
            {
                var clase = clases.Single(c => c.Id == grupo.Key.ClaseId);
                var totalLibro = grupo.Sum(d => d.Importe);
                var cuentaCompartida = clases.Any(c => c.Id != clase.Id && c.CuentaDepreciacionId == clase.CuentaDepreciacionId);
                var contabilizado = poliza?.Estado == EstadoPoliza.Autorizada
                    ? poliza.Partidas.Where(p => p.CuentaId == clase.CuentaDepreciacionId && p.SucursalId == grupo.Key.SucursalId
                        && (p.Dim3Id ?? p.Dim2Id ?? p.Dim1Id) == (grupo.Key.EquipoId ?? grupo.Key.CentroCostoId)).Sum(p => p.Abono-p.Cargo) : 0;
                foreach (var d in grupo)
                {
                    var falta = poliza == null || poliza.Estado != EstadoPoliza.Autorizada;
                    decimal? importe = falta ? 0 : !cuentaCompartida && contabilizado == totalLibro ? d.Importe : null;
                    porActivo.Add(new(d.ActivoId,d.Numero,d.Clase,d.Importe,importe,d.Importe-importe,totalLibro-contabilizado,
                        falta ? "Póliza faltante o anulada" : cuentaCompartida ? "Cuenta compartida: revise el detalle contable por clase" : contabilizado != totalLibro ? "Diferencia en la póliza agrupada por clase y custodia" : "Cuadra con la póliza agrupada por clase y custodia"));
                }
            }
        }
        return new(filas, libro, corridas, operaciones, porActivo);
    }
    private static string Huella(ReporteConciliacion reporte) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        Clases=reporte.Clases.OrderBy(c => c.ClaseId),
        Activos=reporte.Activos.OrderBy(a => a.ActivoId).Select(a => new { a.ActivoId,a.ClaseId,a.SucursalId,a.CentroCostoId,a.EquipoId,a.Valor,a.Acumulada,a.Neto }),
        Depreciacion=reporte.DepreciacionPorActivo?.OrderBy(a => a.ActivoId)
    }))));
    public async Task<ConciliacionLibro> RegistrarAsync(DateOnly periodo, Guid? sucursal, IReadOnlyList<ClasificacionDiferencia> diferencias, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.conciliacion.ver",ct);
        if (diferencias == null || diferencias.Any(d => d == null)) throw new BusinessRuleException("AF_CONCILIACION_CLASIFICACION","Capture las causas y responsables de las diferencias.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct); await servicioLibro.BloquearAsync(ct);
        await using var enlace = await transaccion.VincularAsync(ct);
        if (sucursal == null && await acceso.SucursalesAsync(null,ct) != null) throw new ForbiddenException("AF_SUCURSAL_REQUERIDA","Seleccione una sucursal para registrar la conciliación.");
        var reporte = await ConciliarAsync(periodo,sucursal,ct);
        if (reporte.Clases.Any(c => c.DiferenciaValor == null || c.DiferenciaAcumulada == null)) throw new BusinessRuleException("AF_CONCILIACION_SIN_SALDOS","Complete cuentas distintas por clase antes de aceptar la conciliación.");
        var pendientes = reporte.Clases.SelectMany(c => new[] { (c.ClaseId,Tipo:"Valor",Diferencia:c.DiferenciaValor),(c.ClaseId,Tipo:"Acumulada",Diferencia:c.DiferenciaAcumulada) }).Where(d => d.Diferencia != 0).Select(d => (d.ClaseId,d.Tipo)).ToHashSet();
        if (diferencias.Count != pendientes.Count || diferencias.Select(d => (d.ClaseId,d.Tipo)).Distinct().Count() != diferencias.Count
            || diferencias.Any(d => !pendientes.Contains((d.ClaseId,d.Tipo)) || !Causas.Contains(d.Causa) || d.ResponsableId == Guid.Empty))
            throw new BusinessRuleException("AF_CONCILIACION_CLASIFICACION","Clasifique cada diferencia con su causa y responsable.");
        var aceptadas = new List<ClasificacionAceptada>();
        foreach (var d in diferencias)
        {
            var responsable=await catalogos.ResolverCargaAsync("responsable",d.ResponsableId.ToString(),ct);
            if (responsable.Count != 1) throw new BusinessRuleException("AF_RESPONSABLE_INVALIDO","Seleccione un responsable activo de esta empresa.");
            aceptadas.Add(new(d.ClaseId,reporte.Clases.Single(c => c.ClaseId == d.ClaseId).Clase,d.Tipo,d.Causa,d.ResponsableId,responsable[0].Nombre));
        }
        var huella = Huella(reporte);
        var anterior = await db.Set<ConciliacionLibro>().SingleOrDefaultAsync(c => c.Periodo == CalculoDepreciacion.Mes(periodo) && c.SucursalId == sucursal && c.Huella == huella,ct);
        if (anterior != null) return anterior;
        var registro = new ConciliacionLibro(servicioLibro.Empresa,periodo,sucursal,JsonSerializer.Serialize(reporte),JsonSerializer.Serialize(aceptadas),huella,acceso.Actor,clock.UtcNow);
        db.Add(registro); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return registro;
    }
    public async Task<IReadOnlyList<ConciliacionLibro>> HistorialAsync(DateOnly periodo, Guid? sucursal, CancellationToken ct)
    {
        if (sucursal == null && await acceso.SucursalesAsync(null,ct) != null) throw new ForbiddenException("AF_SUCURSAL_REQUERIDA","Seleccione una sucursal para consultar conciliaciones guardadas.");
        var actual = Huella(await ConciliarAsync(periodo,sucursal,ct));
        var registros = await db.Set<ConciliacionLibro>().AsNoTracking().Where(c => c.Periodo == CalculoDepreciacion.Mes(periodo) && c.SucursalId == sucursal).OrderByDescending(c => c.AceptadaEn).ToListAsync(ct);
        foreach (var c in registros) c.Estado=c.Huella == actual ? "Aceptada" : "Desactualizada";
        return registros;
    }
    public static readonly string[] Causas = ["Carga inicial","Corrida","Disposición","Asiento manual","Póliza faltante"];

}

public sealed record ConciliacionActivo(Guid ActivoId,string Numero,string Clase,decimal ImporteLibro,decimal? ImportePoliza,decimal? Diferencia,decimal DiferenciaGrupo,string CausaSugerida);
public sealed record ClasificacionDiferencia(Guid ClaseId,string Causa,Guid ResponsableId,string Tipo = "Valor");

public sealed record ClasificacionAceptada(Guid ClaseId,string Clase,string Tipo,string Causa,Guid ResponsableId,string Responsable);
