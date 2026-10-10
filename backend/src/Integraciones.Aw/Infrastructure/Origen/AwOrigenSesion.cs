using Millet.Integraciones.Aw.Application.Origen;

namespace Millet.Integraciones.Aw.Infrastructure.Origen;

/// <summary>
/// Scoped: un origen por barrido/ciclo. LeerPendientes y el inicio de cada sincronización renuevan
/// la selección; masters y write-back conservan la misma para no cruzar bases durante el ciclo.
/// No hay caché singleton: el ciclo siguiente lee de nuevo PostgreSQL del ERP.
/// </summary>
public sealed class AwOrigenSesion(IAwOrigenActivo activo)
{
    private AwOrigenEstado? _estado;
    public void Reiniciar() => _estado = null;
    public async Task<bool> EsDemoAsync(CancellationToken ct)
    {
        _estado ??= await activo.LeerAsync(ct);
        if (_estado.Origen != "Demo") return false;
        AwOrigenActivo.VerificarDemo(_estado);
        return true;
    }
}
