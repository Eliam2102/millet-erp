using Millet.Almacen.Application.AppAlmacenes;

namespace Millet.Integraciones.Aw.Infrastructure.AlmMensaje;

/// <summary>
/// Origen simulado de <c>alm_mensaje</c> (por omisión): hace de la tabla y de la app de Almacenes en memoria, con las
/// reglas del acuerdo 5-nov: orden por id, solo el ERP escribe el acuse (una vez), un ERROR frena su referencia
/// hasta que la app reenvía el mensaje con un renglón nuevo de intento+1 (<see cref="Reprocesar"/>). El estado es del proceso: solo desarrollo, demo y pruebas.
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

    /// <summary>La app deja un mensaje (lo que haría un INSERT en alm_mensaje, intento 1). Devuelve el id del renglón.</summary>
    public long Encolar(string mensajeId, string tipo, string? referencia, string sobre, DateTimeOffset? fecha = null)
    {
        lock (_candado)
        {
            var id = _siguiente++;
            _filas.Add(new Fila(id, new AlmMensajeEntrante(id, mensajeId, 1, tipo, referencia, fecha ?? DateTimeOffset.UtcNow, sobre)));
            return id;
        }
    }

    public Task<AlmMensajeSinAcuse> ContarSinAcuseAntiguosAsync(int segundos, CancellationToken ct)
    {
        lock (_candado)
        {
            var ahora = DateTimeOffset.UtcNow;
            var viejos = _filas.GroupBy(f => f.Mensaje.MensajeId).Select(g => g.MaxBy(f => f.Mensaje.Intento)!)
                .Where(f => f.Acuse is null && (ahora - f.Mensaje.Fecha).TotalSeconds > segundos)
                .Select(f => (int)(ahora - f.Mensaje.Fecha).TotalSeconds).ToList();
            return Task.FromResult(new AlmMensajeSinAcuse(viejos.Count, viejos.Count == 0 ? 0 : viejos.Max()));
        }
    }

    /// <summary>Reenvío de la app (v8): renglón NUEVO con el mismo mensajeId, intento+1 y el mismo sobre; el viejo no se toca.</summary>
    public void Reprocesar(string mensajeId)
    {
        lock (_candado)
        {
            var ult = Ultimo(mensajeId);
            var id = _siguiente++;
            _filas.Add(new Fila(id, ult.Mensaje with { Secuencia = id, Intento = ult.Mensaje.Intento + 1 }));
        }
    }

    public AcuseApp? AcuseDe(string mensajeId)
    {
        lock (_candado) return _filas.Any(f => f.Mensaje.MensajeId == mensajeId) ? Ultimo(mensajeId).Acuse : null;
    }

    private Fila Ultimo(string mensajeId) =>
        _filas.Where(f => f.Mensaje.MensajeId == mensajeId).MaxBy(f => f.Mensaje.Intento)!;

    public Task<IReadOnlyList<AlmMensajeEntrante>> LeerPendientesAsync(int max, CancellationToken ct)
    {
        lock (_candado)
        {
            if (FallasDeLectura > 0) { FallasDeLectura--; throw new InvalidOperationException("Simulado: la base de Almacenes no responde."); }
            // Mismas reglas que el SQL: solo el último intento de cada mensaje, orden por el primer renglón del mensaje y
            // un ERROR frena a los siguientes de su referencia mientras ese mensaje siga en ERROR en su último intento.
            var ultimos = _filas.GroupBy(f => f.Mensaje.MensajeId)
                .Select(g => (Fila: g.MaxBy(f => f.Mensaje.Intento)!, Primero: g.Min(f => f.Secuencia))).ToList();
            IReadOnlyList<AlmMensajeEntrante> r = ultimos
                .Where(u => u.Fila.Acuse is null
                    && (u.Fila.Mensaje.Referencia is null
                        || !ultimos.Any(e => e.Fila.Mensaje.Referencia == u.Fila.Mensaje.Referencia && e.Primero < u.Primero
                            && e.Fila.Acuse?.Estado == "ERROR")))
                .OrderBy(u => u.Primero).Take(max).Select(u => u.Fila.Mensaje).ToList();
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
