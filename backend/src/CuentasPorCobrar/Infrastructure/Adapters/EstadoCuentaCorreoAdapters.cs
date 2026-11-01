using System.Net;
using Millet.CuentasPorCobrar.Domain.Ports.Correo;
using Millet.SharedKernel.Application.Correo;

namespace Millet.CuentasPorCobrar.Infrastructure.Adapters;

/// <summary>
/// Estado de cuenta al cliente por el transporte único de correo (C7.8,
/// 86e3txv0n). Con el proveedor NoOp devuelve <c>false</c> y el envío queda
/// Registrado (el PDF ya se descargó en el navegador); un rechazo del
/// proveedor se lanza para que quede Fallido con su motivo.
/// </summary>
public sealed class CorreoSalienteEstadoCuentaAdapter(ICorreoSaliente transporte) : IEstadoCuentaCorreoPort
{
    public const string Modulo = "cuentas-por-cobrar";
    public const string Tipo = "estado-cuenta";

    public async Task<bool> EnviarAsync(CorreoEstadoCuenta correo, CancellationToken cancellationToken)
    {
        var resultado = await transporte.EnviarAsync(Construir(correo), cancellationToken);
        return resultado.Estado switch
        {
            EstadoCorreoSaliente.Aceptado => true,
            EstadoCorreoSaliente.Simulado => false,
            _ => throw new InvalidOperationException($"{resultado.Codigo}: {resultado.Motivo}"),
        };
    }

    public static MensajeCorreo Construir(CorreoEstadoCuenta c)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var mensaje = string.IsNullOrWhiteSpace(c.Mensaje) ? "" : $"<p>{E(c.Mensaje)}</p>";
        var html = "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
                   $"<p>Estimado cliente {E(c.RazonSocialCliente)}:</p>" +
                   "<p>Adjuntamos su estado de cuenta con los movimientos aplicados a la fecha de emisión.</p>" +
                   mensaje +
                   "<p>Si tiene alguna aclaración, responda a este correo.</p><p>Cobranza Millet</p></div>";
        return new MensajeCorreo(null, Modulo, Tipo, null, [c.Destinatario],
            $"Estado de cuenta — {c.RazonSocialCliente}", html,
            [new AdjuntoCorreo(c.NombreArchivo, "application/pdf", c.Pdf)]);
    }
}
