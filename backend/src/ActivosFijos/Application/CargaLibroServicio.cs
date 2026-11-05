using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Application;
public sealed record TotalSap(string Clase, decimal Valor, decimal Acumulada);
public sealed record SolicitudCarga(string Csv, string VersionArchivo, DateOnly Corte, IReadOnlyList<TotalSap> Totales, Guid? SucursalId = null);
public sealed record ErrorCarga(int Renglon, string Codigo, string Mensaje, string? Columna = null);
public sealed record CuadreCarga(string Clase, decimal Valor, decimal? Acumulada, decimal? Neto, decimal? ValorSap, decimal? AcumuladaSap,
    decimal? DiferenciaValor, decimal? DiferenciaAcumulada);
public sealed record ResultadoCarga(bool Valida, IReadOnlyList<ErrorCarga> Errores, IReadOnlyList<CuadreCarga> Cuadre, Guid? CargaId = null, bool Idempotente = false, IReadOnlyList<ErrorCarga>? Avisos = null, IReadOnlyList<ActivoCargaPrevia>? Activos = null);
public sealed record ActivoCargaPrevia(string Numero,string Clase,decimal Valor,decimal? TasaContablePropia,decimal? TasaFiscalPropia,decimal? Acumulada);
public sealed class CargaLibroServicio(ActivosFijosDbContext db, ActivoServicio acceso, LibroServicio libro, ICatalogosActivoReadPort catalogos)
{
    public const string Plantilla = "numero,clase,descripcion,fechaAdquisicion,inicioUso,valorOriginal,depreciacionAcumulada,responsableId,sucursalId,ubicacion,centroCostoId,equipoId,importado,pedimento,arrendado\n";
    public Task<ResultadoCarga> EjecutarAsync(SolicitudCarga request, bool confirmar, CancellationToken ct) =>
        EjecutarTablaAsync(request, () => ArchivoLibro.LeerCsv(request.Csv), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Csv))), confirmar, ct);

    public Task<ResultadoCarga> EjecutarArchivoAsync(SolicitudCarga request, byte[] contenido, string nombre, string? hoja, bool confirmar, CancellationToken ct) =>
        EjecutarTablaAsync(request, () => ArchivoLibro.Leer(contenido, nombre, hoja),
            Convert.ToHexString(SHA256.HashData(contenido)), confirmar, ct);

    private async Task<ResultadoCarga> EjecutarTablaAsync(SolicitudCarga request, Func<TablaLibroArchivo> leer, string huella, bool confirmar, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.carga.ejecutar", ct);
        if (string.IsNullOrWhiteSpace(request.VersionArchivo) || request.Corte == default || Encoding.UTF8.GetByteCount(request.Csv) > ArchivoLibro.LimiteBytes)
            throw new BusinessRuleException("AF_ARCHIVO_INVALIDO", "Capture versión y corte; límite del archivo: 10 MB.");
        if (request.Totales.Any(t => t == null || string.IsNullOrWhiteSpace(t.Clase)))
            throw new BusinessRuleException("AF_TOTALES_INVALIDOS", "Cada total debe indicar una clase, valor y acumulada.");
        var tabla = leer();
        if (tabla.Hoja != null) huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(huella + "\n" + tabla.Hoja)));
        if (tabla.Errores.Count > 0) return new(false, tabla.Errores, [], Avisos: tabla.Avisos);
        await using var tx = confirmar ? await db.Database.BeginTransactionAsync(ct) : null;
        if (confirmar) await libro.BloquearAsync(ct);
        var anterior = await db.Set<CargaLibro>().SingleOrDefaultAsync(x => !x.Anulada && x.Huella == huella && x.VersionArchivo == request.VersionArchivo, ct);
        if (anterior != null)
        {
            if (anterior.Corte != request.Corte) throw new BusinessRuleException("AF_CARGA_CORTE_DISTINTO", "El archivo y versión ya se cargaron con otro corte.");
            // No se exponen totales de otras sucursales a quien no puede consultar todo el lote.
            foreach (var sucursal in await db.Activos.Where(x => x.DocumentoSoporte == $"SAP:{anterior.Id}").Select(x => x.SucursalId).Distinct().ToListAsync(ct))
                await acceso.SucursalAsync(sucursal, true, ct);
            return new(true, [], JsonSerializer.Deserialize<List<CuadreCarga>>(anterior.CuadreJson)!, anterior.Id, true);
        }
        var carga = new CargaLibro { EmpresaId = libro.Empresa, Huella = huella, VersionArchivo = request.VersionArchivo, Corte = request.Corte };
        var errores = new List<ErrorCarga>(); var activos = new List<Activo>(); var sinAcumuladaIds=new HashSet<Guid>();
        var clases = (await db.Clases.Include(x => x.Versiones).Where(x => x.Activa).ToListAsync(ct)).Select(c => c.EnMes(request.Corte)).ToDictionary(c => c.Clave);
        var equivalencias = await db.Set<EquivalenciaClaseSap>().ToDictionaryAsync(x => x.ClaseSap, x => x.ClaveErp, ct);
        foreach (var (sap, erp) in EquivalenciaClaseSap.Iniciales) equivalencias.TryAdd(ArchivoLibro.Normalizar(sap), erp);
        var numeros = (await db.Activos.Select(x => x.Numero).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var avisos = tabla.Avisos.ToList();
        var referencias = new Dictionary<(string, string), IReadOnlyList<ReferenciaCarga>>();
        foreach (var fila in tabla.Filas)
        {
            ct.ThrowIfCancellationRequested();
            var renglon = fila.Numero; var f = fila.Valores; var columna = "número";
            try
            {
                async Task<Guid> Resolver(int indice, string campo, bool opcional)
                {
                    columna = campo;
                    if (string.IsNullOrWhiteSpace(f[indice]) && opcional) return Guid.Empty;
                    var llave = (campo, f[indice]);
                    if (!referencias.TryGetValue(llave, out var candidatos))
                        referencias[llave] = candidatos = await catalogos.ResolverCargaAsync(campo, f[indice], ct);
                    if (candidatos.Count > 1) throw new BusinessRuleException("AF_REFERENCIA_AMBIGUA", $"{campo}: hay varias coincidencias: {string.Join("; ", candidatos.Select(x => x.Nombre))}.");
                    if (candidatos.Count == 1) return candidatos[0].Id;
                    if (campo == "responsable")
                    { avisos.Add(new(renglon, "AF_RESPONSABLE_NO_ENCONTRADO", "No se encontró el responsable; el activo quedará sin responsable.", campo)); return Guid.Empty; }
                    throw new BusinessRuleException("AF_REFERENCIA_NO_EXISTE", $"No se encontró {campo} «{f[indice]}» en el catálogo activo.");
                }
                if (string.IsNullOrWhiteSpace(f[0])) throw new FormatException("Capture el número del activo.");
                if (f[0].Length > 60) throw new FormatException("El número admite hasta 60 caracteres.");
                if (f[0].Equals("EJEMPLO", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Elimine la fila EJEMPLO antes de cargar.");
                if (!numeros.Add(f[0])) throw new BusinessRuleException("AF_NUMERO_DUPLICADO", "Número repetido en el archivo o libro.");
                columna = "clase";
                var coincidencias = clases.Values.Where(c => ArchivoLibro.Normalizar(c.Clave) == ArchivoLibro.Normalizar(f[1]) || ArchivoLibro.Normalizar(c.Nombre) == ArchivoLibro.Normalizar(f[1])).ToList();
                if (coincidencias.Count == 0 && equivalencias.TryGetValue(ArchivoLibro.Normalizar(f[1]), out var claveErp) && clases.TryGetValue(claveErp, out var equivalente)) coincidencias.Add(equivalente);
                if (coincidencias.Count != 1) throw new BusinessRuleException("AF_CLASE_NO_EXISTE", coincidencias.Count == 0 ? "La clase no existe o está inactiva." : $"Clase ambigua: {string.Join("; ", coincidencias.Select(c => c.Clave + " · " + c.Nombre))}.");
                var clase = coincidencias[0];
                columna = "descripción";
                if (string.IsNullOrWhiteSpace(f[2]) || f[2].Length > 500) throw new FormatException("Capture una descripción de hasta 500 caracteres.");
                columna = "fecha de adquisición"; var adquisicion = ArchivoLibro.Fecha(f[3]);
                columna = "inicio de uso"; var inicio = string.IsNullOrWhiteSpace(f[4]) ? adquisicion : ArchivoLibro.Fecha(f[4]);
                if (inicio < adquisicion) throw new FormatException("El inicio de uso no puede ser anterior a la adquisición.");
                if (inicio > request.Corte) throw new FormatException("El inicio de uso no puede ser posterior al corte.");
                columna = "valor"; var valor = ArchivoLibro.Monto(f[5]);
                if (valor <= 0) throw new FormatException("El valor debe ser mayor que cero.");
                columna = "depreciación acumulada";
                var sinAcumulada = string.IsNullOrWhiteSpace(f[6]);
                if (sinAcumulada)
                {
                    var aviso = new ErrorCarga(renglon, "AF_CARGA_SIN_ACUMULADA", $"{f[0]}: falta depreciación acumulada; complétela antes de confirmar.", columna);
                    avisos.Add(aviso); if (confirmar) errores.Add(aviso);
                }
                var acumulada = sinAcumulada ? 0 : ArchivoLibro.Monto(f[6]);
                if (acumulada < 0 || acumulada > decimal.Round(valor, 2)) throw new FormatException("La depreciación acumulada debe estar entre cero y el valor del activo.");
                columna = "importado";
                var importacion = ArchivoLibro.Normalizar(string.IsNullOrWhiteSpace(f[12]) ? f[16] : f[12]) switch { "" or "no" or "nacional" => TipoImportacionActivo.No, "importado" => TipoImportacionActivo.PorConfirmar, "temporal" => TipoImportacionActivo.Temporal, "definitivo" => TipoImportacionActivo.Definitivo, _ => throw new FormatException("Importado debe ser no, temporal o definitivo.") };
                columna = "pedimento";
                if (importacion is TipoImportacionActivo.Temporal or TipoImportacionActivo.Definitivo && string.IsNullOrWhiteSpace(f[13])) throw new FormatException("Capture el pedimento del activo importado.");
                columna = "arrendado";
                if (importacion == TipoImportacionActivo.PorConfirmar) avisos.Add(new(renglon,"AF_IMPORTACION_POR_CONFIRMAR",$"{f[0]}: importado con tipo Por confirmar; complete tipo y pedimento.","origen"));
                var arrendado = ArchivoLibro.Normalizar(string.IsNullOrWhiteSpace(f[14]) ? f[17] : f[14]) switch { "si" or "arrendado" or "rentado" => true, "" or "no" or "propio" => false, _ => throw new FormatException("Arrendado debe ser sí o no.") };
                var responsable = await Resolver(7, "responsable", true);
                var sucursal = string.IsNullOrWhiteSpace(f[8]) && request.SucursalId is Guid destino ? destino : await Resolver(8, "sucursal", false);
                await acceso.SucursalAsync(sucursal, true, ct);
                var centro = await Resolver(10, "centro de costo", true);
                var equipo = await Resolver(11, "equipo", true);
                var custodia = new CustodiaActivo(responsable, sucursal, f[9], centro, equipo == Guid.Empty ? null : equipo);
                columna = "custodia";
                await catalogos.ValidarCustodiaAsync(custodia, false, ct);
                columna = "tasa";
                var tasaContable = string.IsNullOrWhiteSpace(f[18]) ? (decimal?)null : ArchivoLibro.Monto(f[18]);
                var tasaFiscal = string.IsNullOrWhiteSpace(f[19]) ? (decimal?)null : ArchivoLibro.Monto(f[19]);
                if (tasaContable == null && !string.IsNullOrWhiteSpace(f[15]))
                {
                    var mensual = ArchivoLibro.Monto(f[15]);
                    if (mensual < 0) throw new FormatException("La depreciación mensual no puede ser negativa.");
                    var inferida = decimal.Round(mensual * 1200m / valor,4,MidpointRounding.AwayFromZero);
                    if (clase.TasaContableAnual == null || Math.Abs(inferida-clase.TasaContableAnual.Value) > 0.1m)
                    {
                        tasaContable=inferida;
                        avisos.Add(new(renglon,"AF_TASA_PROPIA",FormattableString.Invariant($"{f[0]}: {inferida:0.####} % contra {clase.TasaContableAnual:0.####} % de la clase"),"tasa contable"));
                    }
                }
                if (clase.Clave is "160" or "170" && (tasaContable != null || tasaFiscal != null)) throw new BusinessRuleException("AF_TASA_ACTIVO_INVALIDA","Las clases 160 y 170 requieren vida útil o activo mejorado; no admiten tasa propia.");
                columna = "datos del activo";
                var a = new Activo(Guid.CreateVersion7(), libro.Empresa, f[0], new(f[2], clase.Id, null, null, null, adquisicion, inicio, valor, "MXN", 1, custodia, importacion, f[13], arrendado, $"SAP:{carga.Id}", TasaContableAnual:tasaContable, TasaFiscalAnual:tasaFiscal));
                columna = "depreciación acumulada / corte";
                a.CargarLibro(request.Corte, acumulada); activos.Add(a); if (sinAcumulada) sinAcumuladaIds.Add(a.Id);
            }
            catch (BusinessRuleException e) { errores.Add(new(renglon, e.Code, e.Message, columna)); }
            catch (Exception e) when (e is FormatException or OverflowException) { errores.Add(new(renglon, "AF_FORMATO_INVALIDO", e is OverflowException ? "El importe excede el rango permitido." : e.Message, columna)); }
        }
        if (activos.Count == 0 && errores.Count == 0) errores.Add(new(2, "AF_ARCHIVO_VACIO", "Agregue al menos un activo."));
        if (request.Totales.GroupBy(x => x.Clase).Any(x => x.Count() > 1)) errores.Add(new(0, "AF_TOTALES_DUPLICADOS", "Capture un total por clase."));
        foreach (var total in request.Totales)
            if (!clases.ContainsKey(total.Clase) || total.Valor < 0 || total.Acumulada < 0 || total.Acumulada > total.Valor)
                errores.Add(new(0, "AF_TOTAL_INVALIDO", $"Revise clase y totales de balanza: {total.Clase}."));
        var cuadre = clases.Values.Where(c => activos.Any(a => a.ClaseId == c.Id) || request.Totales.Any(x => x.Clase == c.Clave)).Select(c =>
        {
            var valor = activos.Where(a => a.ClaseId == c.Id).Sum(a => a.ValorActual);
            decimal? acumulada = activos.Any(a => a.ClaseId == c.Id && sinAcumuladaIds.Contains(a.Id)) ? null : activos.Where(a => a.ClaseId == c.Id).Sum(a => a.DepreciacionAcumulada);
            var sap = request.Totales.FirstOrDefault(x => x.Clase == c.Clave);
            return new CuadreCarga(c.Clave, valor, acumulada, valor - acumulada, sap?.Valor, sap?.Acumulada, sap == null ? null : valor - sap.Valor, sap == null ? null : acumulada - sap.Acumulada);
        }).ToList();
        if (confirmar && errores.Count == 0)
        {
            await libro.FechaModificableAsync(request.Corte, ct);
            carga.CuadreJson = JsonSerializer.Serialize(cuadre); db.Add(carga); db.Activos.AddRange(activos);
            await db.SaveChangesAsync(ct); await tx!.CommitAsync(ct);
        }
        return new(errores.Count == 0, errores, cuadre, confirmar && errores.Count == 0 ? carga.Id : null, Avisos: avisos, Activos: activos.Select(a => new ActivoCargaPrevia(a.Numero,clases.Values.Single(c => c.Id == a.ClaseId).Clave,a.ValorOriginal,a.TasaContableAnual,a.TasaFiscalAnual,sinAcumuladaIds.Contains(a.Id) ? null : a.AcumuladaCarga)).ToList());
    }
    public async Task<CargaLibro> AnularAsync(Guid id, int version, string motivo, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.carga.ejecutar",ct);
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500) throw new BusinessRuleException("AF_MOTIVO_REQUERIDO","Capture un motivo de hasta 500 caracteres.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await libro.BloquearAsync(ct);
        var carga = await db.Set<CargaLibro>().SingleAsync(c => c.Id == id,ct);
        if (carga.Anulada) return carga;
        if (carga.Version != version) throw new ConcurrencyException("CargaLibro",id);
        var activos = await db.Activos.Where(a => a.DocumentoSoporte == $"SAP:{id}").ToListAsync(ct);
        foreach (var sucursal in activos.Select(a => a.SucursalId).Distinct()) await acceso.SucursalAsync(sucursal,true,ct);
        var ids = activos.Select(a => a.Id).ToList();
        if (await db.Set<CorridaDepreciacion>().AnyAsync(c => !c.Anulada && c.Detalles.Any(d => ids.Contains(d.ActivoId)),ct))
            throw new BusinessRuleException("AF_CARGA_CON_CORRIDA","Anule las corridas confirmadas de los activos del lote antes de anular la carga.");
        if (await db.Set<OperacionLibro>().AnyAsync(o => ids.Contains(o.ActivoId) && !o.Anulada,ct) || await db.Set<MovimientoValorActivo>().AnyAsync(m => ids.Contains(m.ActivoId),ct))
            throw new BusinessRuleException("AF_CARGA_CON_MOVIMIENTOS","El lote tiene movimientos posteriores; revíselos antes de anularlo.");
        foreach (var a in activos) a.AnularCarga(); carga.Anulada=true; carga.MotivoAnulacion=motivo.Trim();
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return carga;
    }

}
