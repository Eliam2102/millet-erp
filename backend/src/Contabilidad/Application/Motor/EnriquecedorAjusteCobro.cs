using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// CXC-06 (86e3qddef): un pago de cliente (#12) cuyo aplicado supera el depósito solo se contabiliza con un ajuste documentado y
/// autorizado en CxC. El enriquecedor lee ese ajuste por puerto y pone su importe en el campo de su tipo (cuenta por rol). Sin ajuste
/// autorizado no se absorbe el faltante: el evento va a la bandeja de rechazos (dato faltante) y se reprocesa desde ahí.
/// </summary>
public sealed class EnriquecedorAjusteCobro(IAjusteCobroContablePort? ajustes = null) : IEnriquecedorEventoContable
{
    public const string CampoAjuste = "Ajuste";
    public const string CampoComision = "AjusteComision";
    public const string CampoDiferencia = "AjusteDiferencia";
    public const string CampoRedondeo = "AjusteRedondeo";

    public async Task<EventoContable> EnriquecerAsync(EventoContable evento, CancellationToken ct)
    {
        if (evento.Tipo != NormalizadorEventos.PagoClienteConfirmado
            || !evento.Importes.TryGetValue(CampoAjuste, out var valor) || valor is not > 0m)
            return evento;

        var ajuste = valor.Value;
        var propuesta = NormalizadorEventos.PropuestaDelPagoCliente(evento.Payload);
        var autorizado = propuesta is { } id && ajustes is not null ? await ajustes.ObtenerAsync(evento.EmpresaId, id, ct) : null;
        if (autorizado is null)
            throw new RechazoMotorException(CausaRechazoContable.DatoFaltante, "AjusteAutorizado",
                $"El pago aplica {ajuste:N2} más que el depósito y no hay un ajuste autorizado en CxC: no se absorbe el faltante.");

        var importes = new Dictionary<string, decimal?>(evento.Importes)
        {
            [autorizado.Tipo switch
            {
                TipoAjusteCobroContable.ComisionBancaria => CampoComision,
                TipoAjusteCobroContable.Redondeo => CampoRedondeo, // M-326
                _ => CampoDiferencia,
            }] = ajuste,
        };
        return evento with { Importes = importes };
    }
}
