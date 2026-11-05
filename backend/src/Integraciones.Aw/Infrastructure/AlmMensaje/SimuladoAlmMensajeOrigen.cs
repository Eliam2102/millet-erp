using Millet.Almacen.Application.AppAlmacenes;

namespace Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

/// <summary>
/// Origen simulado de <c>alm_mensaje</c> (por omisión): hace de la tabla y de la app de Almacenes en memoria, con las
/// reglas del acuerdo 5-nov: orden por secuencia, solo el ERP escribe el acuse (una vez), un ERROR frena su referencia
/// hasta que la app reprocesa el renglón (<see cref="Reprocesar"/>). El estado es del proceso: solo desarrollo, demo y pruebas.
/// </summary>
public sealed class SimuladoAlmMensajeOrigen : IAlmMensajeOrigen
{
    private sealed class Fila(long secuencia, AlmMensajeEntrante mensaje)
    {
        public long Secuencia { get; } = secuencia;
        public AlmMensajeEntrante Mensaje { get; } = mensaje;
        public AcuseApp? Acuse { get; set; }
    }

    private readonly object _candado = new();
    private readonly List<Fila> _filas = [];
    private long _siguiente = 1;

    /// <summary>Las próximas N lecturas lanzan (la base no responde).</summary>
    public int FallasDeLectura { get; set; }

    /// <summary>La app deja un mensaje (lo que haría un INSERT en alm_mensaje). Devuelve su secuencia.</summary>
    public long Encolar(string mensajeId, string tipo, string? referencia, string sobre, int version = 1)
    {
        lock (_candado)
        {
            var sec = _siguiente++;
            _filas.Add(new Fila(sec, new AlmMensajeEntrante(sec, mensajeId, tipo, version, referencia, DateTimeOffset.UtcNow, sobre)));
            return sec;
        }
    }

    /// <summary>La app reprocesa un renglón acusado: borra el acuse y vuelve a quedar pendiente (el mismo sobre).</summary>
    public void Reprocesar(string mensajeId)
    {
        lock (_candado) _filas.First(f => f.Mensaje.MensajeId == mensajeId).Acuse = null;
    }

    public AcuseApp? AcuseDe(string mensajeId)
    {
        lock (_candado) return _filas.FirstOrDefault(f => f.Mensaje.MensajeId == mensajeId)?.Acuse;
    }

    public Task<IReadOnlyList<AlmMensajeEntrante>> LeerPendientesAsync(int max, CancellationToken ct)
    {
        lock (_candado)
        {
            if (FallasDeLectura > 0) { FallasDeLectura--; throw new InvalidOperationException("Simulado: la base de Almacenes no responde."); }
            IReadOnlyList<AlmMensajeEntrante> r = _filas
                .Where(f => f.Acuse is null
                    && (f.Mensaje.Referencia is null
                        || !_filas.Any(e => e.Mensaje.Referencia == f.Mensaje.Referencia && e.Secuencia < f.Secuencia && e.Acuse?.Estado == "ERROR")))
                .OrderBy(f => f.Secuencia).Take(max).Select(f => f.Mensaje).ToList();
            return Task.FromResult(r);
        }
    }

    public Task EscribirAcuseAsync(long secuencia, AcuseApp acuse, CancellationToken ct)
    {
        lock (_candado)
        {
            var f = _filas.First(x => x.Secuencia == secuencia);
            f.Acuse ??= acuse; // solo si no tenía acuse (como el UPDATE ... WHERE acuse_estado IS NULL)
        }
        return Task.CompletedTask;
    }
}
