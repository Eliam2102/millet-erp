namespace Millet.Compartido.Application.Ports;

/// <summary>
/// Aviso de la descarga del tipo de cambio de Banxico (K10.6) al rol que responde por
/// él. Supuesto a validar con Millet: el Contador General.
/// </summary>
public sealed record AlertaTipoCambio(string Rol, string Asunto, string Detalle)
{
    public const string ContadorGeneral = "Contador General";
}

public interface IAlertaTipoCambioPort
{
    Task NotificarAsync(AlertaTipoCambio alerta, CancellationToken cancellationToken);
}
