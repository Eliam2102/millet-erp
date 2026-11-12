using System.Security.Cryptography;
using System.Text.Json;

namespace Millet.SharedKernel.Application.Notificaciones;

public sealed record ContextoAvisos(Guid UsuarioId, Guid EmpresaId, Guid? SucursalId);

public sealed record AvisoUsuario(
    string ClaveOcurrencia,
    string Modulo,
    string TipoDocumento,
    Guid DocumentoId,
    string Folio,
    string Titulo,
    string Motivo,
    DateTimeOffset FechaOcurrencia,
    string Categoria,
    string Destino,
    Guid? SucursalId = null,
    DateTimeOffset? LeidoOrigenAt = null);

/// <summary>Devuelve el conjunto completo y autorizado; una falla no equivale a una lista vacía.</summary>
public interface IFuenteAvisosUsuario
{
    string Nombre { get; }
    Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct);
    Task MarcarLeidoAsync(ContextoAvisos contexto, AvisoUsuario aviso, CancellationToken ct) => Task.CompletedTask;
}

public static class ClaveAviso
{
    public static string Crear(Guid empresaId, string dominioAccion, string tipoDocumento, Guid documentoId,
        string paso, string ciclo, string episodioAsignacion = "") =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            "1", empresaId.ToString("D"), dominioAccion, tipoDocumento, documentoId.ToString("D"), paso, ciclo, episodioAsignacion,
        })));
}
