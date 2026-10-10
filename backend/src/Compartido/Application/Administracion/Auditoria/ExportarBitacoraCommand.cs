using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Infrastructure.Persistence;
using MediatR;
using Millet.SharedKernel.Application;

namespace Millet.Administracion.Application.Auditoria;

public sealed record ExportarBitacoraCommand(ConsultarBitacoraQuery Filtros) : IRequest<byte[]>;
public sealed class ExportarBitacoraValidator : AbstractValidator<ExportarBitacoraCommand>
{
    public ExportarBitacoraValidator() => RuleFor(x => x.Filtros).SetValidator(new ConsultarBitacoraQueryValidator());
}
public static class BitacoraCsv
{
    public static string Campo(string? valor)
    {
        var v = valor ?? string.Empty;
        if (v.TrimStart().StartsWith('=') || v.TrimStart().StartsWith('+') ||
            v.TrimStart().StartsWith('-') || v.TrimStart().StartsWith('@') ||
            v.StartsWith('\t') || v.StartsWith('\r')) v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
    public static string Fila(AuditLogEntryResponse x) => string.Join(",", new[] {
        x.Timestamp.ToString("O"), x.ActorNombre, x.ActorTipo, x.ActorEmail, x.Modulo,
        x.Entidad, x.EntidadEtiqueta, x.Operacion, x.Resumen, x.SucursalClave,
        x.EntidadId?.ToString(), x.Cambios }.Select(Campo));
}
public sealed class ExportarBitacoraHandler(IMediator mediator, IAuditLogWriter audit,
    ICurrentUserContext user, ICurrentEmpresaContext empresa, CoreDbContext db) : IRequestHandler<ExportarBitacoraCommand, byte[]>
{
    public async Task<byte[]> Handle(ExportarBitacoraCommand request, CancellationToken cancellationToken)
    {
        // La paginación lee una instantánea estable; altas concurrentes no duplican ni omiten filas.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken) : null;
        var csv = new StringBuilder("Fecha UTC,Actor,Tipo de actor,Correo,Módulo,Entidad,Registro,Operación,Resumen,Sucursal,Id de registro,Cambios\r\n");
        var offset = 0;
        ConsultarBitacoraResponse pagina;
        do
        {
            pagina = await mediator.Send(request.Filtros with { Offset = offset, Limit = 200 }, cancellationToken);
            foreach (var fila in pagina.Items) csv.Append(BitacoraCsv.Fila(fila)).Append("\r\n");
            offset += pagina.Items.Count;
        } while (pagina.Items.Count > 0 && offset < pagina.Total);
        await audit.RegistrarAsync("exportar", "Administracion", "Bitacora", null, null,
            user.UserName ?? "Usuario", "usuario", null, "Bitácora de auditoría",
            $"Exportó {offset} registros de auditoría en CSV", user.UserId, empresa.Current,
            cambios: JsonSerializer.Serialize(request.Filtros), cancellationToken: cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
}
