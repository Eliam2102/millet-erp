using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Value object inmutable con la información de importación de la OC
/// (diseño §4.7). Solo aplica cuando <see cref="OrdenCompra.EsImportacion"/>
/// es true.
///
/// Campos requeridos al autorizar: <see cref="IncotermId"/>,
/// <see cref="PaisOrigen"/>, <see cref="NumeroContenedor"/>,
/// <see cref="CodigoRuta"/>, <see cref="SemanaEmbarque"/> (validado por
/// CHECK ck_oc_import_campos a nivel BD). <see cref="NumeroPedimento"/>
/// es opcional al autorizar y editable post-autorización (se captura
/// cuando llega el material).
///
/// Para OCs no-importación, el VO se almacena como null (todas las
/// columnas inline en `ordenes_compra` quedan NULL).
/// </summary>
public readonly record struct InformacionImportacion
{
    public Guid? IncotermId { get; }
    public string? PaisOrigen { get; }
    public string? NumeroContenedor { get; }
    public string? CodigoRuta { get; }
    public string? SemanaEmbarque { get; }
    public string? NumeroPedimento { get; }

    public InformacionImportacion(
        Guid? incotermId = null,
        string? paisOrigen = null,
        string? numeroContenedor = null,
        string? codigoRuta = null,
        string? semanaEmbarque = null,
        string? numeroPedimento = null)
    {
        if (incotermId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "IMPORT_INCOTERM_INVALIDO",
                "IncotermId no puede ser Guid.Empty.");
        }
        if (paisOrigen is not null && paisOrigen.Length != 2)
        {
            throw new BusinessRuleException(
                "IMPORT_PAIS_FORMATO_INVALIDO",
                "PaisOrigen debe ser código ISO 3166-1 alpha-2 (2 caracteres).");
        }
        if (numeroContenedor is { Length: > 40 })
        {
            throw new BusinessRuleException(
                "IMPORT_CONTENEDOR_DEMASIADO_LARGO",
                "NumeroContenedor no puede exceder 40 caracteres.");
        }
        if (codigoRuta is { Length: > 40 })
        {
            throw new BusinessRuleException(
                "IMPORT_RUTA_DEMASIADO_LARGA",
                "CodigoRuta no puede exceder 40 caracteres.");
        }
        if (semanaEmbarque is { Length: > 40 })
        {
            throw new BusinessRuleException(
                "IMPORT_SEMANA_DEMASIADO_LARGA",
                "SemanaEmbarque no puede exceder 40 caracteres.");
        }
        if (numeroPedimento is { Length: > 60 })
        {
            throw new BusinessRuleException(
                "IMPORT_PEDIMENTO_DEMASIADO_LARGO",
                "NumeroPedimento no puede exceder 60 caracteres.");
        }

        IncotermId = incotermId;
        PaisOrigen = string.IsNullOrWhiteSpace(paisOrigen) ? null : paisOrigen.ToUpperInvariant();
        NumeroContenedor = string.IsNullOrWhiteSpace(numeroContenedor) ? null : numeroContenedor;
        CodigoRuta = string.IsNullOrWhiteSpace(codigoRuta) ? null : codigoRuta;
        SemanaEmbarque = string.IsNullOrWhiteSpace(semanaEmbarque) ? null : semanaEmbarque;
        NumeroPedimento = string.IsNullOrWhiteSpace(numeroPedimento) ? null : numeroPedimento;
    }

    public bool EsVacio =>
        IncotermId is null &&
        PaisOrigen is null &&
        NumeroContenedor is null &&
        CodigoRuta is null &&
        SemanaEmbarque is null &&
        NumeroPedimento is null;

    /// <summary>
    /// Valida los campos requeridos al autorizar (§4.7): incoterm,
    /// pais origen, numero contenedor, codigo ruta, semana embarque.
    /// <see cref="NumeroPedimento"/> NO se requiere al autorizar.
    /// Lanza <c>BusinessRuleException</c> si falta cualquiera.
    /// </summary>
    public void ValidarParaAutorizar()
    {
        if (IncotermId is null)
            throw new BusinessRuleException("IMPORT_INCOTERM_REQUERIDO", "IncotermId requerido al autorizar.");
        if (PaisOrigen is null)
            throw new BusinessRuleException("IMPORT_PAIS_REQUERIDO", "PaisOrigen requerido al autorizar.");
        if (NumeroContenedor is null)
            throw new BusinessRuleException("IMPORT_CONTENEDOR_REQUERIDO", "NumeroContenedor requerido al autorizar.");
        if (CodigoRuta is null)
            throw new BusinessRuleException("IMPORT_RUTA_REQUERIDA", "CodigoRuta requerido al autorizar.");
        if (SemanaEmbarque is null)
            throw new BusinessRuleException("IMPORT_SEMANA_REQUERIDA", "SemanaEmbarque requerida al autorizar.");
    }
}
