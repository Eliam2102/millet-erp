namespace Millet.Almacen.Domain.Ports.Notificaciones;

/// <summary>
/// Puerto de salida de los avisos de Almacén.
/// Lo consumen workers como <c>RegularizacionValeSlaWorker</c> (F5-PR1,
/// A14) para avisar al Coordinador y al Jefe de Almacén que un vale
/// urgente sigue sin regularizar tras 24h / 48h.
///
/// <para>
/// Adaptador: <c>CorreoNotificacionService</c> sobre el transporte único
/// de correo (86e3txv0n); destinatarios en <c>Almacen:Avisos</c>.
/// </para>
/// </summary>
public interface INotificacionService
{
    Task NotificarAjusteNivel3AprobadoAsync(
        AvisoAjusteNivel3 aviso, CancellationToken cancellationToken);

    Task NotificarValeSinRegularizarAsync(
        Guid movimientoValeId,
        string folioVale,
        Guid? personaDestinatariaId,
        DateTimeOffset fechaLimite,
        int diaDelSla,
        CancellationToken cancellationToken);
}

/// <summary>Destinatario funcional: Finanzas (<c>Almacen:Avisos:Finanzas</c>).</summary>
public sealed record AvisoAjusteNivel3(Guid EmpresaId, Guid ConteoId,
    decimal MontoNetoMxn, Guid AprobadorId, DateTimeOffset Fecha,
    IReadOnlyList<ConteoAlmacenSucursal> Almacenes);

public sealed record ConteoAlmacenSucursal(Guid AlmacenId, Guid SucursalId);
