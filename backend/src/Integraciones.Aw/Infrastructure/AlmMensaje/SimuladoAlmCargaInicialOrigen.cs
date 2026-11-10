using Millet.Almacen.Application.AppAlmacenes;

namespace Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

/// <summary>
/// Origen simulado de <c>alm_carga_inicial</c> (por omisión): la tabla en memoria, con el acuse por renglón escrito una vez. Solo
/// desarrollo, demo y pruebas.
/// </summary>
public sealed class SimuladoAlmCargaInicialOrigen : IAlmCargaInicialOrigen
{
    private readonly object _candado = new();
    private readonly List<(AlmCargaInicialRenglon Renglon, string? Estado, string? Texto)> _filas = [];

    /// <summary>La app deja un renglón (lo que haría un INSERT en alm_carga_inicial).</summary>
    public void Agregar(AlmCargaInicialRenglon r) { lock (_candado) _filas.Add((r, null, null)); }

    /// <summary>Acuse escrito por el ERP (null = sin acuse).</summary>
    public (string? Estado, string? Texto) Acuse(int cargaId, int renglon)
    {
        lock (_candado)
        {
            var f = _filas.Single(x => x.Renglon.CargaId == cargaId && x.Renglon.Renglon == renglon);
            return (f.Estado, f.Texto);
        }
    }

    public Task<int?> UltimaCargaAsync(CancellationToken ct)
    {
        lock (_candado) return Task.FromResult(_filas.Count == 0 ? null : (int?)_filas.Max(x => x.Renglon.CargaId));
    }

    public Task<int?> CargaAplicadaAsync(CancellationToken ct)
    {
        lock (_candado)
            return Task.FromResult(_filas.Where(x => x.Estado == "APLICADO").Select(x => (int?)x.Renglon.CargaId).DefaultIfEmpty().Max());
    }

    public Task<IReadOnlyList<AlmCargaInicialRenglon>> LeerPendientesAsync(int cargaId, int max, CancellationToken ct)
    {
        lock (_candado)
            return Task.FromResult<IReadOnlyList<AlmCargaInicialRenglon>>(_filas
                .Where(x => x.Renglon.CargaId == cargaId && x.Estado is null).Select(x => x.Renglon)
                .OrderBy(r => r.Renglon).Take(max).ToList());
    }

    public Task EscribirAcuseAsync(int cargaId, int renglon, string estado, string? texto, CancellationToken ct)
    {
        lock (_candado)
        {
            var i = _filas.FindIndex(x => x.Renglon.CargaId == cargaId && x.Renglon.Renglon == renglon && x.Estado is null);
            if (i >= 0) _filas[i] = (_filas[i].Renglon, estado, texto);
        }
        return Task.CompletedTask;
    }
}
