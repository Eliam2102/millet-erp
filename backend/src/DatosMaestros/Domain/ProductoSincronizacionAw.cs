using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>
/// Registro de origen A+W 1:1 con <see cref="ProductoAw"/> (docs/integration/06,
/// patrón de <see cref="ClienteSincronizacionAw"/>): valores crudos recibidos y
/// control de la lectura, sin tocar los datos fiscales del producto.
/// </summary>
public sealed class ProductoSincronizacionAw : BaseEntity, INotAudited
{
    public Guid ProductoAwId { get; private set; }
    public string ReferenciaExterna { get; private set; } = string.Empty;

    public string? DescripcionOrigen { get; private set; }
    public string? UnidadOrigenCruda { get; private set; }
    public string? BajaOrigenCruda { get; private set; }
    public DateTime? TransaccionOrigenUtc { get; private set; }

    // Control
    public Guid? EjecucionId { get; private set; }
    public DateTime LeidoEnUtc { get; private set; }
    public DateTime AplicadoEnUtc { get; private set; }
    public string HashOrigen { get; private set; } = string.Empty;
    public string VersionContrato { get; private set; } = string.Empty;
    public string VersionMapeo { get; private set; } = string.Empty;
    public ResultadoSincronizacionAw Resultado { get; private set; }
    public string? Error { get; private set; }
    /// <summary>JSON de lo recibido que NO se aplicó al producto existente (campo, recibido, conservado, motivo).</summary>
    public string? Diferencias { get; private set; }

    private ProductoSincronizacionAw() { }

    public ProductoSincronizacionAw(Guid id, Guid productoAwId, string referenciaExterna) : base(id)
    {
        if (productoAwId == Guid.Empty)
            throw new BusinessRuleException("PRODUCTO_AW_SYNC_PRODUCTO_INVALIDO", "El producto es requerido.");
        if (string.IsNullOrWhiteSpace(referenciaExterna) || referenciaExterna.Length > 50)
            throw new BusinessRuleException("PRODUCTO_AW_SYNC_REFERENCIA_INVALIDA",
                "La referencia externa es requerida y no puede exceder 50 caracteres.");
        ProductoAwId = productoAwId;
        ReferenciaExterna = referenciaExterna;
    }

    /// <summary>Sobrescribe el registro con la lectura actual; la política sobre <see cref="ProductoAw"/> la decide el servicio.</summary>
    public void Aplicar(
        string? descripcionOrigen, string? unidadOrigenCruda, string? bajaOrigenCruda,
        DateTime? transaccionOrigenUtc,
        Guid? ejecucionId, DateTime leidoEnUtc, DateTime aplicadoEnUtc,
        string hashOrigen, string versionContrato, string versionMapeo,
        ResultadoSincronizacionAw resultado, string? error = null, string? diferencias = null)
    {
        Max(descripcionOrigen, 400, nameof(DescripcionOrigen));
        Max(unidadOrigenCruda, 50, nameof(UnidadOrigenCruda));
        Max(bajaOrigenCruda, 20, nameof(BajaOrigenCruda));
        Max(diferencias, 2000, nameof(Diferencias));
        Max(error, 1000, nameof(Error));
        if (string.IsNullOrWhiteSpace(hashOrigen) || hashOrigen.Length > 64)
            throw new BusinessRuleException("PRODUCTO_AW_SYNC_HASH_INVALIDO",
                "El hash de origen es requerido y no puede exceder 64 caracteres.");
        if (string.IsNullOrWhiteSpace(versionContrato) || versionContrato.Length > 20
            || string.IsNullOrWhiteSpace(versionMapeo) || versionMapeo.Length > 20)
            throw new BusinessRuleException("PRODUCTO_AW_SYNC_VERSION_INVALIDA",
                "Las versiones de contrato y mapeo son requeridas (máx. 20 caracteres).");

        DescripcionOrigen = descripcionOrigen;
        UnidadOrigenCruda = unidadOrigenCruda;
        BajaOrigenCruda = bajaOrigenCruda;
        TransaccionOrigenUtc = transaccionOrigenUtc;
        EjecucionId = ejecucionId;
        LeidoEnUtc = leidoEnUtc;
        AplicadoEnUtc = aplicadoEnUtc;
        HashOrigen = hashOrigen;
        VersionContrato = versionContrato;
        VersionMapeo = versionMapeo;
        Resultado = resultado;
        Error = error;
        Diferencias = diferencias;
    }

    /// <summary>Lectura sin cambios: solo refresca la marca de lectura; conserva el resultado previo.</summary>
    public void RegistrarComprobacion(DateTime leidoEnUtc, Guid? ejecucionId)
    {
        LeidoEnUtc = leidoEnUtc;
        EjecucionId = ejecucionId;
    }

    private static void Max(string? valor, int max, string campo)
    {
        if (valor is not null && valor.Length > max)
            throw new BusinessRuleException("PRODUCTO_AW_SYNC_CAMPO_INVALIDO",
                $"{campo} no puede exceder {max} caracteres.");
    }
}
