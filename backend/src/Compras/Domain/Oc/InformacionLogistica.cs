using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Value object inmutable con la información logística estructurada de la
/// OC (diseño §4.6). Reemplaza el texto libre en comentarios que SAP
/// usaba para codificar RUTA/CONTENEDOR/SEMANA.
///
/// Todos los campos son opcionales. <see cref="NumeroGuia"/> es editable
/// post-autorización (sin re-autorización, §4.6 — se captura durante el
/// ciclo de recepción).
///
/// Invariante: cero o uno de <see cref="TransportistaId"/> y
/// <see cref="TransportistaTexto"/> (no ambos). Si el transportista está
/// en el catálogo, se referencia por id; si no, se captura como texto.
/// </summary>
public readonly record struct InformacionLogistica
{
    public string? DireccionEntrega { get; }
    public Guid? TransportistaId { get; }
    public string? TransportistaTexto { get; }
    public string? NumeroGuia { get; }
    public string? InstruccionesEnvio { get; }

    public InformacionLogistica(
        string? direccionEntrega = null,
        Guid? transportistaId = null,
        string? transportistaTexto = null,
        string? numeroGuia = null,
        string? instruccionesEnvio = null)
    {
        if (transportistaId is not null && !string.IsNullOrWhiteSpace(transportistaTexto))
        {
            throw new BusinessRuleException(
                "LOGISTICA_TRANSPORTISTA_AMBIGUO",
                "No se pueden capturar TransportistaId y TransportistaTexto simultáneamente.");
        }
        if (transportistaId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "LOGISTICA_TRANSPORTISTA_INVALIDO",
                "TransportistaId no puede ser Guid.Empty.");
        }
        if (transportistaTexto is { Length: > 200 })
        {
            throw new BusinessRuleException(
                "LOGISTICA_TRANSPORTISTA_TEXTO_LARGO",
                "TransportistaTexto no puede exceder 200 caracteres.");
        }
        if (numeroGuia is { Length: > 80 })
        {
            throw new BusinessRuleException(
                "LOGISTICA_GUIA_DEMASIADO_LARGA",
                "NumeroGuia no puede exceder 80 caracteres.");
        }

        DireccionEntrega = string.IsNullOrWhiteSpace(direccionEntrega) ? null : direccionEntrega;
        TransportistaId = transportistaId;
        TransportistaTexto = string.IsNullOrWhiteSpace(transportistaTexto) ? null : transportistaTexto;
        NumeroGuia = string.IsNullOrWhiteSpace(numeroGuia) ? null : numeroGuia;
        InstruccionesEnvio = string.IsNullOrWhiteSpace(instruccionesEnvio) ? null : instruccionesEnvio;
    }

    public bool EsVacio =>
        DireccionEntrega is null &&
        TransportistaId is null &&
        TransportistaTexto is null &&
        NumeroGuia is null &&
        InstruccionesEnvio is null;
}
