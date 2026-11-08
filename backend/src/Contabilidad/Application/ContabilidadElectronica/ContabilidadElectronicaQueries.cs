using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.Reportes;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.ContabilidadElectronica;

// K10.8 (F1-CON-13): revisión previa y generación de los XML de contabilidad electrónica. Solo lectura: no guarda nada ni cambia
// pólizas. Cuentan las pólizas autorizadas y las anuladas con su anulación, igual que la balanza de C1.7; los borradores no.

public sealed record RevisionContabilidadElectronicaResponse(
    int Anio, int Mes, string? Rfc, bool RfcValido, string EstadoPeriodo, int CuentasCatalogo, IReadOnlyList<ProblemaCuentaCe> Problemas,
    bool PuedeGenerarCatalogo, int Polizas, int PolizasConCfdi, IReadOnlyList<string> Avisos);

/// <summary>Revisión antes de generar (K10.8-a): RFC, estado del periodo, cuentas sin código agrupador y CFDI de las pólizas.</summary>
public sealed record RevisionContabilidadElectronicaQuery(int Anio, int Mes) : IRequest<RevisionContabilidadElectronicaResponse>;

public sealed class RevisionContabilidadElectronicaValidator : AbstractValidator<RevisionContabilidadElectronicaQuery>
{
    public RevisionContabilidadElectronicaValidator()
    {
        RuleFor(q => q.Anio).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo);
        RuleFor(q => q.Mes).InclusiveBetween(1, PeriodoContable.NumeroAjuste);
    }
}

public sealed record ArchivoContabilidadElectronica(string NombreArchivo, string ContentType, byte[] Contenido);

/// <summary>
/// Genera un archivo de contabilidad electrónica. <c>Mes</c> 13 solo para la balanza de cierre. La balanza complementaria lleva la
/// fecha de modificación; las pólizas, el tipo de solicitud con su número de orden (AF/FC) o de trámite (DE/CO).
/// <c>Zip</c> = el .zip que pide el buzón tributario, con el XML dentro y el mismo nombre.
/// </summary>
public sealed record GenerarContabilidadElectronicaQuery(
    TipoArchivoContabilidadElectronica Tipo, int Anio, int Mes, TipoEnvioBalanza TipoEnvio = TipoEnvioBalanza.Normal,
    DateOnly? FechaModificacion = null, TipoSolicitudPolizas? Solicitud = null, string? NumOrden = null, string? NumTramite = null,
    bool Zip = true) : IRequest<ArchivoContabilidadElectronica>;

public sealed class GenerarContabilidadElectronicaValidator : AbstractValidator<GenerarContabilidadElectronicaQuery>
{
    public GenerarContabilidadElectronicaValidator()
    {
        RuleFor(q => q.Tipo).IsInEnum();
        RuleFor(q => q.Anio).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo);
        RuleFor(q => q.Mes).InclusiveBetween(1, 12).When(q => q.Tipo != TipoArchivoContabilidadElectronica.Balanza)
            .WithMessage("El mes debe ser de 1 a 12 (el 13 solo aplica a la balanza de cierre).");
        RuleFor(q => q.Mes).InclusiveBetween(1, PeriodoContable.NumeroAjuste).When(q => q.Tipo == TipoArchivoContabilidadElectronica.Balanza);
        RuleFor(q => q.TipoEnvio).IsInEnum();
        RuleFor(q => q.FechaModificacion).NotNull()
            .When(q => q.Tipo == TipoArchivoContabilidadElectronica.Balanza && q.TipoEnvio == TipoEnvioBalanza.Complementaria)
            .WithMessage("La balanza complementaria lleva la fecha de la última modificación.");
        RuleFor(q => q.FechaModificacion).GreaterThanOrEqualTo(new DateOnly(2015, 1, 1)).When(q => q.FechaModificacion is not null)
            .WithMessage("La fecha de modificación no puede ser anterior a 2015.");
        RuleFor(q => q.Solicitud).NotNull().IsInEnum().When(q => q.Tipo == TipoArchivoContabilidadElectronica.Polizas)
            .WithMessage("Indica el tipo de solicitud de las pólizas (AF, FC, DE o CO).");
        RuleFor(q => q.NumOrden).NotEmpty().Matches("^[A-Z]{3}[0-9]{7}/[0-9]{2}$")
            .When(q => q.Tipo == TipoArchivoContabilidadElectronica.Polizas && q.Solicitud is TipoSolicitudPolizas.AF or TipoSolicitudPolizas.FC)
            .WithMessage("El número de orden tiene la forma ABC1234567/12.");
        RuleFor(q => q.NumTramite).NotEmpty().Matches("^[A-Z]{2}[0-9]{12}$")
            .When(q => q.Tipo == TipoArchivoContabilidadElectronica.Polizas && q.Solicitud is TipoSolicitudPolizas.DE or TipoSolicitudPolizas.CO)
            .WithMessage("El número de trámite tiene la forma AB123456789012.");
    }
}

/// <summary>Lecturas comunes de la revisión y la generación.</summary>
public sealed class LecturaContabilidadElectronica(
    ContabilidadDbContext db, LecturaReportesContables reportes, IEmpresaContabilidadReadPort empresas,
    IComprobanteFiscalContabilidadReadPort comprobantes, ICurrentEmpresaContext empresa)
{
    public async Task<string?> RfcAsync(CancellationToken ct)
    {
        var id = empresa.Current ?? throw new BusinessRuleException("CONTAB_CE_SIN_EMPRESA", "No hay empresa en la sesión.");
        return (await empresas.RfcAsync(id, ct))?.Trim().ToUpperInvariant();
    }

    public Task<List<CuentaContable>> CuentasAsync(CancellationToken ct) => db.Cuentas.AsNoTracking().ToListAsync(ct);

    public async Task<string> EstadoPeriodoAsync(int anio, int mes, CancellationToken ct)
    {
        var estado = await db.Periodos.AsNoTracking().Where(p => p.Anio == anio && p.Numero == mes)
            .Select(p => (EstadoPeriodo?)p.Estado).FirstOrDefaultAsync(ct);
        return estado switch
        {
            EstadoPeriodo.Cerrado => "Cerrado",
            EstadoPeriodo.Abierto => "Abierto",
            EstadoPeriodo.NoAbierto => "No abierto",
            _ => "Sin ejercicio",
        };
    }

    public async Task<IReadOnlyList<FilaBalanzaCe>> FilasBalanzaAsync(int anio, int mes, CancellationToken ct)
    {
        var movs = reportes.Movimientos(null);
        var iniciales = await LecturaReportesContables.Antes(movs, anio, mes)
            .GroupBy(m => m.CuentaId).Select(g => new { Cuenta = g.Key, Saldo = g.Sum(m => m.Cargo - m.Abono) })
            .ToDictionaryAsync(x => x.Cuenta, x => x.Saldo, ct);
        var periodo = await LecturaReportesContables.Entre(movs, anio, mes, mes)
            .GroupBy(m => m.CuentaId).Select(g => new { Cuenta = g.Key, Cargos = g.Sum(m => m.Cargo), Abonos = g.Sum(m => m.Abono) })
            .ToDictionaryAsync(x => x.Cuenta, x => (x.Cargos, x.Abonos), ct);
        return GeneradorContabilidadElectronica.FilasBalanza(await CuentasAsync(ct), iniciales, periodo);
    }

    public Task<int> ContarPolizasAsync(int anio, int mes, bool soloConCfdi, CancellationToken ct) =>
        PolizasDelMes(anio, mes).Where(p => !soloConCfdi || p.OrigenUuid != null).CountAsync(ct);

    public async Task<IReadOnlyList<PolizaCe>> PolizasAsync(int anio, int mes, CancellationToken ct)
    {
        var polizas = await PolizasDelMes(anio, mes)
            .OrderBy(p => p.Fecha).ThenBy(p => p.TipoClave).ThenBy(p => p.Numero)
            .Select(p => new { p.Id, p.TipoClave, p.Numero, p.Fecha, p.Concepto, p.OrigenUuid })
            .ToListAsync(ct);
        var ids = polizas.Select(p => p.Id).ToList();
        var partidas = (await (from x in db.PolizasPartidas.AsNoTracking()
                               join c in db.Cuentas.AsNoTracking() on x.CuentaId equals c.Id
                               where ids.Contains(x.PolizaId)
                               orderby x.Linea
                               select new { x.PolizaId, x.CuentaCodigo, c.Nombre, x.Concepto, x.Cargo, x.Abono })
                .ToListAsync(ct))
            .ToLookup(x => x.PolizaId);
        return [.. polizas.Select(p => new PolizaCe(
            LecturaReportesContables.Folio(p.TipoClave, p.Numero) ?? p.Id.ToString(), p.Fecha, p.Concepto, p.OrigenUuid,
            [.. partidas[p.Id].Select(x => new PartidaCe(x.CuentaCodigo, x.Nombre, x.Concepto, x.Cargo, x.Abono))]))];
    }

    public async Task<IReadOnlyDictionary<string, ComprobanteNacionalContable>> ComprobantesAsync(IEnumerable<PolizaCe> polizas, CancellationToken ct)
    {
        var uuids = polizas.Where(p => !string.IsNullOrWhiteSpace(p.Uuid)).Select(p => p.Uuid!.Trim().ToUpperInvariant())
            .Distinct().ToList();
        return uuids.Count == 0 ? new Dictionary<string, ComprobanteNacionalContable>() : await comprobantes.ObtenerAsync(uuids, ct);
    }

    private IQueryable<Poliza> PolizasDelMes(int anio, int mes) =>
        db.Polizas.AsNoTracking().Where(p => p.PeriodoAnio == anio && p.PeriodoNumero == mes && p.Estado != EstadoPoliza.Borrador);
}

public sealed class RevisionContabilidadElectronicaHandler(LecturaContabilidadElectronica lectura)
    : IRequestHandler<RevisionContabilidadElectronicaQuery, RevisionContabilidadElectronicaResponse>
{
    public async Task<RevisionContabilidadElectronicaResponse> Handle(RevisionContabilidadElectronicaQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rfc = await lectura.RfcAsync(ct);
        var rfcValido = GeneradorContabilidadElectronica.EsRfcValido(rfc);
        var cuentas = await lectura.CuentasAsync(ct);
        var problemas = GeneradorContabilidadElectronica.Revisar(cuentas);
        var estado = await lectura.EstadoPeriodoAsync(request.Anio, request.Mes, ct);

        var avisos = new List<string>();
        if (!rfcValido)
            avisos.Add(rfc is null ? "La empresa no tiene RFC capturado; no se puede generar ningún archivo." : $"El RFC de la empresa ({rfc}) no tiene el formato del SAT.");
        if (estado != "Cerrado")
            avisos.Add($"El periodo está {estado.ToLowerInvariant()}: la balanza y las pólizas pueden cambiar después de generarlas.");

        int polizas = 0, conCfdi = 0;
        if (request.Mes <= 12)
        {
            polizas = await lectura.ContarPolizasAsync(request.Anio, request.Mes, false, ct);
            conCfdi = await lectura.ContarPolizasAsync(request.Anio, request.Mes, true, ct);
            if (conCfdi > 0)
            {
                var delMes = await lectura.PolizasAsync(request.Anio, request.Mes, ct);
                GeneradorContabilidadElectronica.Polizas(rfc ?? "XAXX010101000", request.Anio, request.Mes, TipoSolicitudPolizas.AF, null, null,
                    delMes, await lectura.ComprobantesAsync(delMes, ct), avisos);
            }
        }
        else
            avisos.Add("El periodo 13 solo genera la balanza de cierre; el catálogo y las pólizas se piden por mes de 1 a 12.");

        return new RevisionContabilidadElectronicaResponse(request.Anio, request.Mes, rfc, rfcValido, estado,
            GeneradorContabilidadElectronica.CuentasCatalogo(cuentas).Count, problemas, rfcValido && problemas.Count == 0, polizas, conCfdi, avisos);
    }
}

public sealed class GenerarContabilidadElectronicaHandler(LecturaContabilidadElectronica lectura)
    : IRequestHandler<GenerarContabilidadElectronicaQuery, ArchivoContabilidadElectronica>
{
    public async Task<ArchivoContabilidadElectronica> Handle(GenerarContabilidadElectronicaQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rfc = await lectura.RfcAsync(ct);
        if (!GeneradorContabilidadElectronica.EsRfcValido(rfc))
            throw new BusinessRuleException("CONTAB_CE_RFC_INVALIDO",
                rfc is null ? "La empresa no tiene RFC capturado." : $"El RFC de la empresa ({rfc}) no tiene el formato del SAT.");

        XDocument xml;
        string sufijo;
        switch (request.Tipo)
        {
            case TipoArchivoContabilidadElectronica.Catalogo:
                var cuentas = await lectura.CuentasAsync(ct);
                var problemas = GeneradorContabilidadElectronica.Revisar(cuentas);
                if (problemas.Count > 0)
                    throw new BusinessRuleException("CONTAB_CE_CATALOGO_INCOMPLETO",
                        $"Hay {problemas.Select(p => p.CuentaId).Distinct().Count()} cuenta(s) sin código agrupador SAT válido o sin naturaleza. " +
                        "Revísalas en la lista antes de generar el catálogo.");
                xml = GeneradorContabilidadElectronica.Catalogo(rfc!, request.Anio, request.Mes, cuentas);
                sufijo = "CT";
                break;
            case TipoArchivoContabilidadElectronica.Balanza:
                xml = GeneradorContabilidadElectronica.Balanza(rfc!, request.Anio, request.Mes, request.TipoEnvio, request.FechaModificacion,
                    await lectura.FilasBalanzaAsync(request.Anio, request.Mes, ct));
                sufijo = request.TipoEnvio == TipoEnvioBalanza.Complementaria ? "BC" : "BN";
                break;
            default:
                var polizas = await lectura.PolizasAsync(request.Anio, request.Mes, ct);
                if (polizas.Count == 0)
                    throw new BusinessRuleException("CONTAB_CE_SIN_POLIZAS", "El periodo no tiene pólizas autorizadas.");
                xml = GeneradorContabilidadElectronica.Polizas(rfc!, request.Anio, request.Mes, request.Solicitud!.Value, request.NumOrden,
                    request.NumTramite, polizas, await lectura.ComprobantesAsync(polizas, ct), []);
                sufijo = "PL";
                break;
        }

        var errores = EsquemasSat.Validar(xml);
        if (errores.Count > 0)
            throw new BusinessRuleException("CONTAB_CE_XML_INVALIDO",
                "El XML no cumple el esquema del SAT: " + string.Join(" | ", errores.Take(3)));

        var nombre = GeneradorContabilidadElectronica.NombreArchivo(rfc!, request.Anio, request.Mes, sufijo);
        var contenido = Bytes(xml);
        return request.Zip
            ? new ArchivoContabilidadElectronica(nombre + ".zip", "application/zip", Comprimir(nombre + ".xml", contenido))
            : new ArchivoContabilidadElectronica(nombre + ".xml", "application/xml", contenido);
    }

    internal static byte[] Bytes(XDocument xml)
    {
        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
            xml.Save(w);
        return ms.ToArray();
    }

    private static byte[] Comprimir(string entrada, byte[] contenido)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry(entrada, CompressionLevel.Optimal).Open();
            s.Write(contenido);
        }
        return ms.ToArray();
    }
}

/// <summary>Lee los parámetros de texto del endpoint de generación y responde 400 en español si alguno no es válido.</summary>
public static class ParametrosContabilidadElectronica
{
    public static GenerarContabilidadElectronicaQuery Leer(string tipo, int anio, int mes, string? tipoEnvio, string? fechaModificacion,
        string? solicitud, string? numOrden, string? numTramite, string? formato)
    {
        var errores = new List<ValidationError>();
        TipoArchivoContabilidadElectronica archivo = default;
        switch (tipo.Trim().ToLowerInvariant())
        {
            case "catalogo": archivo = TipoArchivoContabilidadElectronica.Catalogo; break;
            case "balanza": archivo = TipoArchivoContabilidadElectronica.Balanza; break;
            case "polizas": archivo = TipoArchivoContabilidadElectronica.Polizas; break;
            default: errores.Add(new("tipo", "TIPO_INVALIDO", $"El archivo «{tipo}» no existe; usa catalogo, balanza o polizas.")); break;
        }
        var envio = TipoEnvioBalanza.Normal;
        switch (tipoEnvio?.Trim().ToUpperInvariant())
        {
            case null or "" or "N" or "NORMAL": break;
            case "C" or "COMPLEMENTARIA": envio = TipoEnvioBalanza.Complementaria; break;
            default: errores.Add(new("tipoEnvio", "TIPO_ENVIO_INVALIDO", "El tipo de envío es N (normal) o C (complementaria).")); break;
        }
        DateOnly? fecha = null;
        if (!string.IsNullOrWhiteSpace(fechaModificacion))
        {
            if (DateOnly.TryParseExact(fechaModificacion, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var f)) fecha = f;
            else errores.Add(new("fechaModificacion", "FECHA_INVALIDA", $"La fecha «{fechaModificacion}» no es válida; usa el formato aaaa-mm-dd."));
        }
        TipoSolicitudPolizas? tipoSolicitud = null;
        if (!string.IsNullOrWhiteSpace(solicitud))
        {
            if (Enum.TryParse<TipoSolicitudPolizas>(solicitud.Trim(), ignoreCase: true, out var s) && Enum.IsDefined(s) && !int.TryParse(solicitud, out _))
                tipoSolicitud = s;
            else errores.Add(new("solicitud", "SOLICITUD_INVALIDA", "El tipo de solicitud es AF, FC, DE o CO."));
        }
        var zip = true;
        switch (formato?.Trim().ToLowerInvariant())
        {
            case null or "" or "zip": break;
            case "xml": zip = false; break;
            default: errores.Add(new("formato", "FORMATO_INVALIDO", "El formato es zip o xml.")); break;
        }
        if (errores.Count > 0) throw new Millet.SharedKernel.Application.Exceptions.ValidationException(errores);
        return new GenerarContabilidadElectronicaQuery(archivo, anio, mes, envio, fecha, tipoSolicitud,
            string.IsNullOrWhiteSpace(numOrden) ? null : numOrden.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(numTramite) ? null : numTramite.Trim().ToUpperInvariant(), zip);
    }
}
