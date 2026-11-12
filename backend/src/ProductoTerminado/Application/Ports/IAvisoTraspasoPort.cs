namespace Millet.ProductoTerminado.Application.Ports;

/// <summary>
/// Aviso de pendiente por recepción (M-249, Fernando Alejos 11-nov): al autorizarse un traspaso, la sucursal destino recibe un correo para que lo reciba.
/// Los destinatarios son los usuarios de esa sucursal con el permiso de recibir; Api lo implementa sobre Identidad y el transporte único de correo.
/// </summary>
public interface IAvisoTraspasoPort
{
    Task AvisarPorRecibirAsync(AvisoTraspasoPorRecibir aviso, CancellationToken ct);
}

public sealed record AvisoTraspasoPorRecibir(Guid EmpresaId, Guid TraspasoId, string Folio, Guid SucursalDestinoId, int Piezas);
