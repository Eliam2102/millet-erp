using System.Text.Json;
using Millet.Compras.Application.Integration.AlmCompra;

namespace Millet.Integraciones.Aw.Infrastructure.AlmCompra;

/// <summary>
/// Transporte simulado ERP → Almacenes (por omisión, 86e3jvu83): hace de la tabla <c>alm_compra</c> y de la app de Almacenes en
/// memoria, con las reglas que dijo Jorge (29-oct): <c>mensajeId</c> único (repetido = sin efecto), COMPRA_RECIBIDA de un folio ya
/// registrado = DUPLICADO con el mismo compraId, CANCELADA/MODIFICADA de una compra desconocida, cancelada o con entrada cerrada = ERROR.
/// El estado es del proceso: se pierde al reiniciar (solo desarrollo, demo y pruebas).
/// <para>
/// Perillas para las pruebas: <see cref="FallasPendientes"/> (las próximas N escrituras lanzan), <see cref="LecturasAntesDeAcuse"/>
/// (acuse lento), <see cref="Forzar"/> (acuse fijo para un mensaje), <see cref="MarcarEntradaCerrada"/> y <see cref="Reprocesar"/>
/// (Almacenes reprocesa un ERROR desde su app). <see cref="ViolacionesDeOrden"/> anota toda escritura que llegó mientras el mensaje
/// anterior de su referencia no estaba procesado.
/// </para>
/// </summary>
public sealed class SimuladoAlmCompraTransporte : IAlmCompraTransporte
{
    private sealed class Fila(AlmCompraMensaje mensaje, string folio)
    {
        public AlmCompraMensaje Mensaje { get; } = mensaje;
        public string Folio { get; } = folio;
        public AlmCompraAcuse Acuse { get; set; } = null!;
        public int Lecturas { get; set; }
    }

    private readonly object _candado = new();
    private readonly List<Fila> _filas = [];
    private readonly Dictionary<string, string> _compraPorFolio = [];
    private readonly HashSet<string> _canceladas = [];
    private readonly HashSet<string> _entradaCerrada = [];
    private readonly Dictionary<string, (string Estado, string? Error)> _forzados = [];
    private int _siguienteCompraId = 1;

    public int FallasPendientes { get; set; }
    public int LecturasAntesDeAcuse { get; set; }

    /// <summary>MensajeIds escritos, en orden de llegada (sin repetidos).</summary>
    public IReadOnlyList<string> Escrituras { get { lock (_candado) return _filas.Select(f => f.Mensaje.MensajeId).ToList(); } }
    public List<string> ViolacionesDeOrden { get; } = [];

    public void Forzar(string mensajeId, string estado, string? error = null) { lock (_candado) _forzados[mensajeId] = (estado, error); }
    public void MarcarEntradaCerrada(string folio) { lock (_candado) _entradaCerrada.Add(folio); }

    /// <summary>Almacenes reprocesa el mensaje desde su app (sin el error forzado): el acuse se recalcula.</summary>
    public void Reprocesar(string mensajeId)
    {
        lock (_candado)
        {
            _forzados.Remove(mensajeId);
            var f = _filas.First(x => x.Mensaje.MensajeId == mensajeId);
            f.Acuse = Calcular(f);
        }
    }

    public Task EscribirAsync(AlmCompraMensaje mensaje, CancellationToken ct)
    {
        lock (_candado)
        {
            if (FallasPendientes > 0)
            {
                FallasPendientes--;
                throw new IOException("Transporte simulado: falla al escribir en alm_compra.");
            }
            if (_filas.Any(f => f.Mensaje.MensajeId == mensaje.MensajeId)) return Task.CompletedTask; // mensaje_id único
            var anterior = _filas.LastOrDefault(f => f.Mensaje.Referencia == mensaje.Referencia);
            if (anterior is not null && anterior.Acuse.Estado is not ("PROCESADO" or "DUPLICADO"))
                ViolacionesDeOrden.Add($"{mensaje.MensajeId} llegó con {anterior.Mensaje.MensajeId} en {anterior.Acuse.Estado}");
            using var doc = JsonDocument.Parse(mensaje.Sobre);
            var fila = new Fila(mensaje, doc.RootElement.GetProperty("datos").GetProperty("folio").GetString()!);
            fila.Acuse = Calcular(fila);
            _filas.Add(fila);
        }
        return Task.CompletedTask;
    }

    public Task<AlmCompraAcuse?> LeerAcuseAsync(string mensajeId, CancellationToken ct)
    {
        lock (_candado)
        {
            var f = _filas.FirstOrDefault(x => x.Mensaje.MensajeId == mensajeId);
            if (f is null || f.Lecturas++ < LecturasAntesDeAcuse) return Task.FromResult<AlmCompraAcuse?>(null);
            return Task.FromResult<AlmCompraAcuse?>(f.Acuse);
        }
    }

    private AlmCompraAcuse Calcular(Fila f)
    {
        var ahora = DateTimeOffset.UtcNow;
        if (_forzados.TryGetValue(f.Mensaje.MensajeId, out var forzado))
            return new(forzado.Estado, forzado.Estado == "ERROR" ? null : "1", forzado.Error, ahora);
        var folio = f.Folio;
        if (f.Mensaje.Tipo == "COMPRA_RECIBIDA")
        {
            if (_compraPorFolio.TryGetValue(folio, out var existente)) return new("DUPLICADO", existente, null, ahora);
            var id = (_siguienteCompraId++).ToString();
            _compraPorFolio[folio] = id;
            return new("PROCESADO", id, null, ahora);
        }
        if (!_compraPorFolio.TryGetValue(folio, out var compra)) return new("ERROR", null, $"La compra {folio} no existe.", ahora);
        if (_canceladas.Contains(folio)) return new("ERROR", null, $"La compra {folio} está cancelada.", ahora);
        if (_entradaCerrada.Contains(folio)) return new("ERROR", null, $"La compra {folio} ya tiene entrada cerrada.", ahora);
        if (f.Mensaje.Tipo == "COMPRA_CANCELADA") _canceladas.Add(folio);
        return new("PROCESADO", compra, null, ahora);
    }
}
