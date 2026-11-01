using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.SharedKernel.Infrastructure.Correo;

/// <summary>
/// Modo pruebas: cambia cada destinatario fuera de los dominios permitidos por
/// los buzones de prueba, marca el asunto con «[PRUEBAS]» y pone arriba del
/// cuerpo a quién iba. Si todos los destinatarios están permitidos, el correo
/// pasa sin cambios.
/// </summary>
public sealed class ModoPruebasCorreoSaliente(ICorreoSaliente interno, ModoPruebasCorreoOptions opciones) : ICorreoSaliente
{
    public const string PrefijoAsunto = "[PRUEBAS] ";

    public bool EsSimulado => interno.EsSimulado;

    public Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken) =>
        interno.EnviarAsync(Redirigir(mensaje, opciones), cancellationToken);

    public static MensajeCorreo Redirigir(MensajeCorreo mensaje, ModoPruebasCorreoOptions opciones)
    {
        if (!opciones.Activo) return mensaje;
        var permitidos = mensaje.Para.Where(p => EsDominioPermitido(p, opciones.DominiosPermitidos)).ToList();
        if (permitidos.Count == mensaje.Para.Count) return mensaje;

        var para = permitidos.Concat(opciones.BuzonesPrueba)
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var aviso = "<p style='font-family:Arial,sans-serif;background:#FFF4E5;border:1px solid #F5A623;padding:8px'>" +
                    "Correo del servidor de pruebas. Destinatario original: <strong>" +
                    WebUtility.HtmlEncode(string.Join(", ", mensaje.Para)) + "</strong></p>";
        return mensaje with
        {
            Para = para,
            Asunto = PrefijoAsunto + mensaje.Asunto,
            Html = aviso + mensaje.Html,
            ParaOriginal = mensaje.Para,
        };
    }

    public static bool EsDominioPermitido(string correo, IEnumerable<string> dominios)
    {
        var arroba = correo.LastIndexOf('@');
        if (arroba < 0) return false;
        var dominio = correo[(arroba + 1)..].Trim();
        return dominios.Any(d => dominio.Equals(d.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Registra cada intento en <c>core.correo_envio</c> (qué, a quién, cuándo y
/// con qué resultado). Una excepción del transporte se convierte en
/// <see cref="EstadoCorreoSaliente.Error"/> con su motivo: el envío no se pierde
/// y el módulo decide si reintenta.
/// </summary>
public sealed class RegistroCorreoSaliente(
    ICorreoSaliente interno,
    string proveedor,
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<RegistroCorreoSaliente> logger) : ICorreoSaliente
{
    public bool EsSimulado => interno.EsSimulado;

    public async Task<ResultadoCorreoSaliente> EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        ResultadoCorreoSaliente resultado;
        if (mensaje.Para.Count == 0)
            resultado = ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.SinDestinatarios, "El correo no tiene destinatarios.");
        else
        {
            try
            {
                resultado = await interno.EnviarAsync(mensaje, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                resultado = ResultadoCorreoSaliente.Error(CodigosCorreoSaliente.ErrorEnvio, ex.Message);
            }
        }

        if (resultado.Estado == EstadoCorreoSaliente.Error)
            logger.LogWarning("[Correo] {Modulo}/{Tipo} {Referencia} no se envió: {Codigo} {Motivo}",
                mensaje.Modulo, mensaje.Tipo, mensaje.ReferenciaId, resultado.Codigo, resultado.Motivo);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            db.CorreoEnvios.Add(CorreoEnvio.Desde(mensaje, resultado, proveedor, clock.UtcNow));
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // El registro no debe convertir un correo enviado en un fallo (y provocar un duplicado al reintentar).
            logger.LogError(ex, "[Correo] No se pudo registrar el envío {Modulo}/{Tipo} {Referencia}.",
                mensaje.Modulo, mensaje.Tipo, mensaje.ReferenciaId);
        }
        return resultado;
    }
}

/// <summary>Fila de <c>core.correo_envio</c>: un intento de envío. No guarda los adjuntos.</summary>
public sealed class CorreoEnvio
{
    public Guid Id { get; private set; }
    public Guid? EmpresaId { get; private set; }
    public string Modulo { get; private set; } = string.Empty;
    public string Tipo { get; private set; } = string.Empty;
    public Guid? ReferenciaId { get; private set; }

    /// <summary>A quién iba (antes del modo pruebas).</summary>
    public string ParaOriginal { get; private set; } = string.Empty;

    /// <summary>A quién salió de verdad.</summary>
    public string Para { get; private set; } = string.Empty;

    public string Asunto { get; private set; } = string.Empty;
    public string Estado { get; private set; } = string.Empty;
    public string? Codigo { get; private set; }
    public string? Motivo { get; private set; }
    public int Adjuntos { get; private set; }
    public long BytesAdjuntos { get; private set; }
    public string Proveedor { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private CorreoEnvio() { }

    public static CorreoEnvio Desde(MensajeCorreo m, ResultadoCorreoSaliente r, string proveedor, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        EmpresaId = m.EmpresaId,
        Modulo = Recortar(m.Modulo, 40)!,
        Tipo = Recortar(m.Tipo, 60)!,
        ReferenciaId = m.ReferenciaId,
        ParaOriginal = Recortar(string.Join(", ", m.ParaOriginal ?? m.Para), 2000)!,
        Para = Recortar(string.Join(", ", m.Para), 2000)!,
        Asunto = Recortar(m.Asunto, 300)!,
        Estado = r.Estado switch
        {
            EstadoCorreoSaliente.Aceptado => "aceptado",
            EstadoCorreoSaliente.Simulado => "simulado",
            _ => "error",
        },
        Codigo = Recortar(r.Codigo, 60),
        Motivo = Recortar(r.Motivo, 1000),
        Adjuntos = m.AdjuntosOVacio.Count,
        BytesAdjuntos = m.AdjuntosOVacio.Sum(a => (long)a.Contenido.Length),
        Proveedor = Recortar(proveedor, 20)!,
        CreatedAt = ahora,
    };

    private static string? Recortar(string? valor, int max) =>
        valor is null ? null : valor.Length <= max ? valor : valor[..max];
}
