using System.Collections.Concurrent;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.NotasCredito;

/// <summary>Notas de A+W ya aplicadas en este proceso: no se vuelven a procesar en cada barrido. Singleton; al reiniciar se vacía y la idempotencia del handler (por número de nota) evita duplicar.</summary>
public sealed class AwNotasCreditoVistas
{
    private readonly ConcurrentDictionary<int, byte> _aplicadas = new();
    public bool Contiene(int numeroNota) => _aplicadas.ContainsKey(numeroNota);
    public void Marcar(int numeroNota) => _aplicadas.TryAdd(numeroNota, 0);
}

/// <summary>
/// <see cref="IAwNotasCreditoPort"/> sobre la lectura directa de A+W (<see cref="AwNotasCreditoLectorAdapter"/>, solo SELECT).
/// El acuse NO se escribe en A+W: no hay tabla ni columna donde hacerlo y el ERP no escribe en A+W. Una nota aplicada se recuerda
/// en memoria; una rechazada se relee en el siguiente barrido hasta que salga de la ventana (la factura puede timbrarse después).
/// ponytail: sin cursor persistente; si la ventana creciera mucho, guardar la última fecha procesada.
/// </summary>
public sealed class AwNotasCreditoDirectoPort(AwNotasCreditoLectorAdapter lector, AwNotasCreditoLecturaOptions opciones, AwNotasCreditoVistas vistas)
    : IAwNotasCreditoPort
{
    /// <summary>La nota de A+W no trae un id de solicitud: se deriva del número (determinista, reversible).</summary>
    internal static Guid SolicitudDe(int numeroNota) => new(numeroNota, 0, 0, new byte[8]);

    public async Task<IReadOnlyList<SolicitudNotaCreditoAw>> LeerNotasPendientesAsync(int max, CancellationToken cancellationToken)
    {
        var desde = DateTime.UtcNow.AddDays(-Math.Max(1, opciones.VentanaDias));
        var notas = await lector.LeerAsync(desde, cancellationToken);
        // max del worker (50) se ignora: con un tope fijo y las más antiguas primero, 50 rechazadas dejarían sin leer a las demás.
        return notas
            .Where(n => !vistas.Contiene(n.NumeroNota))
            .Take(Math.Max(1, opciones.MaxPorLectura))
            .Select(n => new SolicitudNotaCreditoAw(
                SolicitudDe(n.NumeroNota), n.NumeroNota.ToString(), Version: 1, n.NumeroPedido.ToString(), FacturaFolio: null,
                n.ClienteRef.ToString(), n.Motivo, n.Importe ?? 0m, Descripcion: null,
                new DateTimeOffset(DateTime.SpecifyKind(n.FechaEstatus95, DateTimeKind.Local)),
                n.Vendedor, n.EsRanura))
            .ToList();
    }

    public Task EscribirAcuseAsync(AcuseNotaCreditoAw acuse, CancellationToken cancellationToken)
    {
        if (acuse.Resultado == ResultadoSolicitudAw.Aplicada)
            vistas.Marcar(BitConverter.ToInt32(acuse.SolicitudId.ToByteArray(), 0));
        return Task.CompletedTask;
    }
}
