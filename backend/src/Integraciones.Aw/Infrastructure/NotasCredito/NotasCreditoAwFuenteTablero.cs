using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Integraciones.Aw.Infrastructure.NotasCredito;

/// <summary>
/// M-454: el lector de notas de crédito de A+W en el tablero de integraciones. Solo se registra con
/// <c>NotasCreditoLectura:Habilitada=true</c>; si falta <c>ConnectionStrings:AwClientesDb</c> (o es una referencia de
/// Key Vault sin resolver) el lector no corre y aquí queda un mensaje en error, que abre la alerta del tablero y
/// deja el error en el log desde el primer ciclo tras arrancar, en vez de omitirse en silencio.
/// </summary>
public sealed class NotasCreditoAwFuenteTablero(bool conCadena, ILogger<NotasCreditoAwFuenteTablero> logger) : IFuenteTableroIntegracion
{
    internal static readonly string ErrorSinCadena =
        $"Lector de notas de crédito de A+W encendido sin la cadena ConnectionStrings:{AwClientesOptions.ConnectionStringName}: no se leen notas de crédito. Configura la cadena (Key Vault) o apaga IntegracionesAw:NotasCreditoLectura:Habilitada.";

    public string Clave => "aw.notas-credito";
    public string Nombre => "Notas de crédito de A+W";
    public string Grupo => "A+W";
    public bool PermiteReintento => false;

    public Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        if (conCadena) return Task.FromResult(ResumenFuenteIntegracion.Vacio);
        logger.LogError("[M-454] {Error}", ErrorSinCadena);
        return Task.FromResult(new ResumenFuenteIntegracion(0, 0, 1, 0, null, null,
            [new MensajeIntegracion("configuracion", "Configuración del lector", EstadoMensajeIntegracion.Error, 0, ErrorSinCadena, ahora, null)]));
    }

    public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken) => Task.FromResult(false);
}
