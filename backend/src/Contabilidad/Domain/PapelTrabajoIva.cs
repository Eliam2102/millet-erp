using System.Text.Json;
using Millet.Contabilidad.Application.Iva;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>
/// M-311 (CXP-10-07): foto <b>inmutable</b> de la liquidación de IVA del mes, guardada al generar su póliza (resumen, detalle por
/// documento y cuadre). No tiene mutadores, EF no actualiza ninguna columna tras el alta y un disparador de la base rechaza
/// UPDATE y DELETE: un pago revertido después no la cambia; lo que cambie contra la foto se registra como <see cref="DiferenciaFiscalIva"/>.
/// </summary>
public sealed class LiquidacionIvaFoto : IPerteneceAEmpresa
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public int Anio { get; private set; }
    public int Mes { get; private set; }
    public Guid PolizaId { get; private set; }
    public string FolioPoliza { get; private set; } = string.Empty;
    public string ResumenJson { get; private set; } = "{}";
    public string DetalleJson { get; private set; } = "[]";
    public string CuadreJson { get; private set; } = "[]";
    public string TratamientosJson { get; private set; } = "[]";
    public DateTimeOffset CreadaEn { get; private set; }
    public Guid CreadaPor { get; private set; }

    private LiquidacionIvaFoto() { }

    public static LiquidacionIvaFoto Crear(Guid empresaId, LiquidacionIvaResponse liquidacion, Guid polizaId, string folioPoliza, Guid creadaPor,
        DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(), EmpresaId = empresaId, Anio = liquidacion.Anio, Mes = liquidacion.Mes, PolizaId = polizaId,
        FolioPoliza = folioPoliza, CreadaEn = ahora, CreadaPor = creadaPor,
        ResumenJson = JsonSerializer.Serialize(liquidacion.Resumen, Json),
        DetalleJson = JsonSerializer.Serialize(liquidacion.Documentos, Json),
        CuadreJson = JsonSerializer.Serialize(liquidacion.Cuadre, Json),
        TratamientosJson = JsonSerializer.Serialize(liquidacion.Tratamientos ?? [], Json),
    };

    public ResumenLiquidacionIva Resumen() => JsonSerializer.Deserialize<ResumenLiquidacionIva>(ResumenJson, Json)!;
    public IReadOnlyList<DocumentoLiquidacionIva> Detalle() => JsonSerializer.Deserialize<List<DocumentoLiquidacionIva>>(DetalleJson, Json)!;
    public IReadOnlyList<CuadreRolIva> Cuadre() => JsonSerializer.Deserialize<List<CuadreRolIva>>(CuadreJson, Json)!;
}

/// <summary>M-311: acuse de la declaración (un solo archivo por foto; no se reemplaza). Tabla aparte para que la foto no cambie.</summary>
public sealed class LiquidacionIvaAcuse : IPerteneceAEmpresa
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid FotoId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string TipoContenido { get; private set; } = string.Empty;
    public byte[] Contenido { get; private set; } = [];
    public string Sha256 { get; private set; } = string.Empty;
    public DateTimeOffset AdjuntadoEn { get; private set; }
    public Guid AdjuntadoPor { get; private set; }

    private LiquidacionIvaAcuse() { }

    public static LiquidacionIvaAcuse Crear(Guid empresaId, Guid fotoId, string nombre, string tipoContenido, byte[] contenido, Guid por,
        DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new BusinessRuleException("IVA_ACUSE_NOMBRE", "El acuse necesita nombre de archivo.");
        if (contenido.Length == 0 || contenido.Length > MaxBytes)
            throw new BusinessRuleException("IVA_ACUSE_TAMANO", "El acuse debe pesar entre 1 byte y 5 MB.");
        return new()
        {
            Id = Guid.CreateVersion7(), EmpresaId = empresaId, FotoId = fotoId, Nombre = nombre.Trim(), TipoContenido = tipoContenido,
            Contenido = contenido, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contenido)),
            AdjuntadoEn = ahora, AdjuntadoPor = por,
        };
    }
}

public enum EstadoDiferenciaFiscalIva : short { Pendiente = 1, Cerrada = 2 }

/// <summary>M-311 (CXP-10-R): diferencia entre la foto y lo que el mes muestra ahora; queda pendiente hasta ligarla a su póliza de ajuste.</summary>
public sealed class DiferenciaFiscalIva : IPerteneceAEmpresa
{
    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid FotoId { get; private set; }
    public Guid SaldoIvaDocumentoId { get; private set; }
    public string? Folio { get; private set; }
    public string? Uuid { get; private set; }
    public decimal IvaFoto { get; private set; }
    public decimal IvaActual { get; private set; }
    public decimal Diferencia { get; private set; }
    public string Causa { get; private set; } = string.Empty;
    public EstadoDiferenciaFiscalIva Estado { get; private set; } = EstadoDiferenciaFiscalIva.Pendiente;
    public Guid? AjustePolizaId { get; private set; }
    public DateTimeOffset DetectadaEn { get; private set; }
    public DateTimeOffset? CerradaEn { get; private set; }
    public Guid? CerradaPor { get; private set; }

    private DiferenciaFiscalIva() { }

    public static DiferenciaFiscalIva Detectar(Guid empresaId, Guid fotoId, CambioContraFoto c, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(), EmpresaId = empresaId, FotoId = fotoId, SaldoIvaDocumentoId = c.SaldoId, Folio = c.Folio, Uuid = c.Uuid,
        IvaFoto = c.IvaFoto, IvaActual = c.IvaActual, Diferencia = c.IvaActual - c.IvaFoto, Causa = c.Causa, DetectadaEn = ahora,
    };

    /// <summary>Mientras siga pendiente, si el mes se movió otra vez se actualiza el importe y la causa.</summary>
    public void Actualizar(CambioContraFoto c)
    {
        if (Estado != EstadoDiferenciaFiscalIva.Pendiente) return;
        IvaActual = c.IvaActual; Diferencia = c.IvaActual - IvaFoto; Causa = c.Causa;
    }

    public void LigarAjuste(Guid polizaId, Guid por, DateTimeOffset ahora)
    {
        if (Estado == EstadoDiferenciaFiscalIva.Cerrada)
            throw new ConflictException("IVA_DIFERENCIA_CERRADA", "La diferencia ya está ligada a su ajuste.");
        if (polizaId == Guid.Empty)
            throw new BusinessRuleException("IVA_DIFERENCIA_AJUSTE", "Indique la póliza de ajuste que corrige la diferencia.");
        AjustePolizaId = polizaId; CerradaPor = por; CerradaEn = ahora; Estado = EstadoDiferenciaFiscalIva.Cerrada;
    }
}

/// <summary>Un documento cuyo IVA del mes ya no coincide con la foto.</summary>
public sealed record CambioContraFoto(Guid SaldoId, string? Folio, string? Uuid, decimal IvaFoto, decimal IvaActual, string Causa);

/// <summary>M-311: compara la foto contra la liquidación viva del mismo mes (función pura, sin base de datos).</summary>
public static class DetectorDiferenciasIva
{
    public const string CausaPagoRevertido = "Pago o cobro revertido o ajustado después de la liquidación";
    public const string CausaPagoNuevo = "Pago o cobro aplicado después de la liquidación";

    public static IReadOnlyList<CambioContraFoto> Comparar(IReadOnlyList<DocumentoLiquidacionIva> foto, IReadOnlyList<DocumentoLiquidacionIva> actual)
    {
        var vivos = actual.ToDictionary(d => d.SaldoId);
        var cambios = new List<CambioContraFoto>();
        foreach (var f in foto)
        {
            var ahora = vivos.GetValueOrDefault(f.SaldoId)?.IvaMes ?? 0m;
            if (ahora != f.IvaMes) cambios.Add(new(f.SaldoId, f.Folio, f.Uuid, f.IvaMes, ahora, ahora > f.IvaMes ? CausaPagoNuevo : CausaPagoRevertido));
        }
        var enFoto = foto.Select(f => f.SaldoId).ToHashSet();
        cambios.AddRange(actual.Where(a => !enFoto.Contains(a.SaldoId) && a.IvaMes != 0m)
            .Select(a => new CambioContraFoto(a.SaldoId, a.Folio, a.Uuid, 0m, a.IvaMes, CausaPagoNuevo)));
        return cambios;
    }
}
