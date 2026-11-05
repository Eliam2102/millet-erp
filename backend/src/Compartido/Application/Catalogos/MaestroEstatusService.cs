using System.Text.Json;
using FluentValidation;
using Millet.Compartido.Application.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos;

/// <summary>Reglas comunes al inactivar/reactivar un maestro: revisar lo abierto y dejar el motivo en la bitácora.</summary>
public sealed class MaestroEstatusService(
    IEnumerable<IPendientesMaestroReadPort> fuentes,
    IAuditLogWriter audit,
    ICurrentUserContext user,
    ICurrentEmpresaContext empresa)
{
    private const int MaxPendientes = 10;

    /// <summary>Regla de motivo compartida por los validadores de los seis comandos (5 a 500 caracteres).</summary>
    public static IRuleBuilderOptions<T, string> ReglaMotivo<T>(IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("El motivo es obligatorio.")
        .MinimumLength(5).WithMessage("El motivo debe contener al menos 5 caracteres.")
        .MaximumLength(500).WithMessage("El motivo no puede exceder 500 caracteres.");

    public async Task ExigirSinPendientesAsync(TipoMaestro tipo, Guid id, string nombre, CancellationToken ct)
    {
        var pendientes = new List<string>();
        foreach (var f in fuentes) pendientes.AddRange(await f.ObtenerAsync(tipo, id, ct));
        if (pendientes.Count == 0) return;

        var lista = string.Join(" ", pendientes.Take(MaxPendientes));
        var resto = pendientes.Count > MaxPendientes ? $" Y {pendientes.Count - MaxPendientes} más." : "";
        throw new BusinessRuleException("MAESTRO_CON_PENDIENTES",
            $"No se puede inactivar {nombre}: tiene pendientes. {lista}{resto}");
    }

    public Task RegistrarAsync(string operacion, string entidad, Guid id, string etiqueta, bool inactivar,
        string motivo, CancellationToken ct) => audit.RegistrarAsync(
            operacion: operacion,
            modulo: "DatosMaestros",
            entidad: entidad,
            entidadId: id,
            aggregateRootId: id,
            actorNombre: user.UserName ?? "Usuario",
            actorTipo: "usuario",
            actorEmail: user.Email,
            entidadEtiqueta: etiqueta,
            resumen: $"{entidad} {etiqueta} {(inactivar ? "inactivado" : "reactivado")}. Motivo: {motivo}",
            usuarioId: user.UserId ?? Guid.Empty,
            empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new
            {
                estatusAnterior = inactivar ? "Activo" : "Inactivo",
                nuevoEstatus = inactivar ? "Inactivo" : "Activo",
                motivo,
            }),
            cancellationToken: ct);
}
