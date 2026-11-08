using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Compras.Domain.AlmCompra;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Envíos de compras de materia prima hacia la app de Almacenes (F1-COM-09) en el
/// tablero de integraciones (I13.6). El worker ya reintenta con espera creciente
/// (5 s … 15 min) y pasa a ERROR al agotar intentos; el reintento manual es el
/// mismo del tablero de envíos de Compras.
/// </summary>
public sealed class AlmCompraFuenteTablero(ComprasDbContext db, AlmCompraEncolador encolador) : IFuenteTableroIntegracion
{
    public string Clave => "compras.envios-almacenes";
    public string Nombre => "Compras de materia prima hacia Almacenes";
    public string Grupo => "Almacenes";
    public bool PermiteReintento => true;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var desde = ahora.AddHours(-24);
        var envios = db.AlmCompraEnvios.AsNoTracking();
        var abiertos = envios.Where(e => e.Estado == AlmCompraEnvio.Pendiente || e.Estado == AlmCompraEnvio.Enviado);

        var pendientes = await abiertos.CountAsync(e => e.Intentos == 0, ct);
        var reintentando = await abiertos.CountAsync(e => e.Intentos > 0, ct);
        var enError = await envios.CountAsync(e => e.Estado == AlmCompraEnvio.Error, ct);
        var cerrados = envios.Where(e => e.Estado == AlmCompraEnvio.Procesado || e.Estado == AlmCompraEnvio.Duplicado
            || e.Estado == AlmCompraEnvio.Rechazado);
        var ok = await cerrados.CountAsync(e => e.AcuseEn >= desde, ct);
        var ultimoExito = await cerrados.MaxAsync(e => e.AcuseEn, ct);
        var masAntiguo = await abiertos.OrderBy(e => e.Secuencia)
            .Select(e => (DateTimeOffset?)e.CreatedAt).FirstOrDefaultAsync(ct);

        var lista = await envios
            .OrderBy(e => e.Estado == AlmCompraEnvio.Error ? 0
                : e.Estado == AlmCompraEnvio.Pendiente || e.Estado == AlmCompraEnvio.Enviado ? 1 : 2)
            .ThenByDescending(e => e.Secuencia)
            .Take(maxMensajes).ToListAsync(ct);
        var mensajes = lista.Select(e => new MensajeIntegracion(
            e.Id.ToString(), $"{e.Tipo} · {e.Referencia}", Estado(e), e.Intentos, e.UltimoError, e.CreatedAt,
            e.AcuseEn ?? e.EnviadoEn)).ToList();
        return new ResumenFuenteIntegracion(pendientes, reintentando, enError, ok, ultimoExito, masAntiguo, mensajes);
    }

    public async Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(mensajeId, out var id)) return false;
        var envio = await db.AlmCompraEnvios.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (envio is null || envio.Estado != AlmCompraEnvio.Error || envio.EnviadoEn is not null) return false;
        string? sobre = null;
        if (envio.Sobre.Length == 0)
            sobre = await encolador.ReconstruirSobreAsync(envio, cancellationToken)
                ?? throw new BusinessRuleException("ALM_COMPRA_SIN_LINEAS_APP", "La OC ya no tiene líneas que surtan una requisición de la app.");
        envio.Reintentar(sobre);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static EstadoMensajeIntegracion Estado(AlmCompraEnvio e) => e.Estado switch
    {
        AlmCompraEnvio.Error => EstadoMensajeIntegracion.Error,
        AlmCompraEnvio.Pendiente or AlmCompraEnvio.Enviado => e.Intentos > 0
            ? EstadoMensajeIntegracion.Reintentando
            : EstadoMensajeIntegracion.Pendiente,
        _ => EstadoMensajeIntegracion.Ok,
    };
}
