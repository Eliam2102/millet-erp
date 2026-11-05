using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Avisos;

/// <summary>
/// Aviso dentro del ERP (campana del usuario) generado por Almacén (86e3tac2y). Una fila por destinatario; la llave
/// (usuario, tipo, referencia) es única para que un mismo vale o ajuste avise una sola vez.
/// </summary>
public sealed class AvisoAlmacen : IPerteneceAEmpresa
{
    public const string TipoValeVencido = "vale-vencido";
    public const string TipoAjusteMayor = "ajuste-mayor";
    public const string TipoActaPorRevisar = "acta-por-revisar";

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public Guid UsuarioDestinoId { get; private set; }
    public string Tipo { get; private set; } = string.Empty;
    public Guid ReferenciaId { get; private set; }
    public string Titulo { get; private set; } = string.Empty;
    public string Mensaje { get; private set; } = string.Empty;
    public string? Ruta { get; private set; }
    public DateTimeOffset CreadoAt { get; private set; }
    public DateTimeOffset? LeidoAt { get; private set; }

    private AvisoAlmacen() { }

    public AvisoAlmacen(Guid empresaId, Guid usuarioDestinoId, string tipo, Guid referenciaId, string titulo, string mensaje, string? ruta)
    {
        Id = Guid.CreateVersion7();
        EmpresaId = empresaId;
        UsuarioDestinoId = usuarioDestinoId;
        Tipo = tipo;
        ReferenciaId = referenciaId;
        Titulo = titulo;
        Mensaje = mensaje;
        Ruta = ruta;
        CreadoAt = DateTimeOffset.UtcNow;
    }

    public void MarcarLeido() => LeidoAt ??= DateTimeOffset.UtcNow;
}
