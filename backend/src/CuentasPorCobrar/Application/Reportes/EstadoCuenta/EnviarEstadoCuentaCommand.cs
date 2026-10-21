using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.EstadoCuenta;
using Millet.CuentasPorCobrar.Domain.Ports.Correo;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Reportes.EstadoCuenta;

// ============================================================================
// F1-CXC-09 · C7.8: emitir y enviar el estado de cuenta al cliente. El PDF
// lo genera el frontend con el exportador del reporte (ADR-0036); el
// backend recalcula el bloqueo de CA7.14, registra el envío con la huella
// del PDF y los totales del corte, y lo entrega al puerto de correo. Un
// fallo del correo no revierte el registro: queda en estado Fallido.
// ============================================================================

/// <summary>Totales del corte por moneda — nunca se suman MXN y USD.</summary>
public sealed record TotalMonedaEstadoCuenta(string Moneda, decimal Cargo, decimal Abono, decimal Saldo);

public sealed record EstadoCuentaEnvioResponse(
    Guid Id,
    Guid ClienteId,
    string Destinatario,
    string? Mensaje,
    Guid UsuarioId,
    string? UsuarioNombre,
    DateTimeOffset Fecha,
    string Estado,
    string? DetalleError,
    int NumeroFilas,
    string NombreArchivo,
    string PdfSha256,
    IReadOnlyList<TotalMonedaEstadoCuenta> Totales);

internal static class EstadoCuentaEnvioMapper
{
    public static EstadoCuentaEnvioResponse ToResponse(EstadoCuentaEnvio e) =>
        new(e.Id, e.ClienteId, e.Destinatario, e.Mensaje, e.UsuarioId, e.UsuarioNombre, e.Fecha,
            e.Estado.ToString(), e.DetalleError, e.NumeroFilas, e.NombreArchivo, e.PdfSha256,
            JsonSerializer.Deserialize<List<TotalMonedaEstadoCuenta>>(e.TotalesJson, JsonOpciones) ?? []);

    public static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);
}

public sealed record EnviarEstadoCuentaCommand(
    Guid ClienteId,
    string Destinatario,
    string? Mensaje,
    string NombreArchivo,
    string ContentType,
    byte[] Pdf) : IRequest<EstadoCuentaEnvioResponse>;

public sealed class EnviarEstadoCuentaValidator : AbstractValidator<EnviarEstadoCuentaCommand>
{
    /// <summary>10 MB: un estado de cuenta tabular rara vez supera unos cientos de KB.</summary>
    public const int TamanoMaximoBytes = 10 * 1024 * 1024;

    public EnviarEstadoCuentaValidator()
    {
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.Destinatario).NotEmpty().MaximumLength(320)
            .Matches(@"^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$")
            .WithMessage("Captura un correo válido para el destinatario.");
        RuleFor(c => c.Mensaje).MaximumLength(2000);
        RuleFor(c => c.NombreArchivo).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ContentType).Equal("application/pdf")
            .WithMessage("El estado de cuenta debe adjuntarse en PDF.");
        RuleFor(c => c.Pdf).NotNull()
            .Must(p => p is { Length: > 0 }).WithMessage("Adjunta el PDF del estado de cuenta.")
            .Must(p => p is null || p.Length <= TamanoMaximoBytes).WithMessage("El PDF excede 10 MB.")
            .Must(p => p is null || p.Length == 0 || p.AsSpan().StartsWith("%PDF-"u8))
            .WithMessage("El archivo adjunto no es un PDF válido.");
    }
}

public sealed class EnviarEstadoCuentaHandler(
    CuentasPorCobrarDbContext db,
    IFacturacionAnticiposReadPort anticipos,
    IClienteReadPort clientes,
    IClock clock,
    ICurrentEmpresaContext currentEmpresa,
    ICurrentUserContext currentUser,
    IEstadoCuentaCorreoPort correo,
    IReppPendientesReadPort? repp = null)
    : IRequestHandler<EnviarEstadoCuentaCommand, EstadoCuentaEnvioResponse>
{
    public async Task<EstadoCuentaEnvioResponse> Handle(
        EnviarEstadoCuentaCommand command, CancellationToken cancellationToken)
    {
        if (currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        if (currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que envía.");

        // CA7.14: el bloqueo se recalcula en servidor; nunca se confía en la pantalla.
        await new EstadoCuentaBloqueo(db, repp).VerificarEmitibleAsync(command.ClienteId, cancellationToken);

        // Mismo corte que ve el usuario (el alcance por sucursal ya lo validó el endpoint).
        var reporte = await new EstadoCuentaClienteHandler(db, anticipos, clientes, clock)
            .Handle(new EstadoCuentaClienteQuery(command.ClienteId), cancellationToken);
        var totales = reporte.Filas
            .GroupBy(f => f.GetValueOrDefault("moneda") as string ?? "MXN")
            .OrderBy(g => g.Key)
            .Select(g => new TotalMonedaEstadoCuenta(g.Key,
                g.Sum(f => f.GetValueOrDefault("cargo") as decimal? ?? 0m),
                g.Sum(f => f.GetValueOrDefault("abono") as decimal? ?? 0m),
                // Saldo = solo facturas (el saldo de anticipo es informativo, no cartera).
                g.Where(f => f.GetValueOrDefault("tipo") as string == "Factura")
                    .Sum(f => f.GetValueOrDefault("saldo_documento") as decimal? ?? 0m)))
            .ToList();

        var envio = EstadoCuentaEnvio.Registrar(
            empresaId, command.ClienteId, command.Destinatario, command.Mensaje,
            usuarioId, currentUser.UserName ?? currentUser.Email, clock.UtcNow,
            JsonSerializer.Serialize(totales, EstadoCuentaEnvioMapper.JsonOpciones),
            reporte.Filas.Count, Convert.ToHexStringLower(SHA256.HashData(command.Pdf)),
            command.NombreArchivo, command.Pdf.LongLength);
        db.EstadoCuentaEnvios.Add(envio);
        await db.SaveChangesAsync(cancellationToken);

        var razonSocial = reporte.FiltrosAplicados.FirstOrDefault(f => f.Label == "Cliente")?.Valor
            ?? command.ClienteId.ToString();
        try
        {
            if (await correo.EnviarAsync(new CorreoEstadoCuenta(envio.Destinatario, razonSocial, envio.Mensaje,
                    envio.NombreArchivo, command.Pdf), cancellationToken))
                envio.MarcarEnviado();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            envio.MarcarFallido(ex.Message);
        }

        if (envio.Estado != EstadoEnvioEstadoCuenta.Registrado)
            await db.SaveChangesAsync(CancellationToken.None);
        return EstadoCuentaEnvioMapper.ToResponse(envio);
    }
}

// --------------------------------------------------- Historial

public sealed record ListarEnviosEstadoCuentaQuery(Guid ClienteId)
    : IRequest<IReadOnlyList<EstadoCuentaEnvioResponse>>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "cliente_cartera";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ListarEnviosEstadoCuentaHandler(CuentasPorCobrarDbContext db)
    : IRequestHandler<ListarEnviosEstadoCuentaQuery, IReadOnlyList<EstadoCuentaEnvioResponse>>
{
    public async Task<IReadOnlyList<EstadoCuentaEnvioResponse>> Handle(
        ListarEnviosEstadoCuentaQuery query, CancellationToken cancellationToken)
    {
        EstadoCuentaAlcance.Verificar(query.ClienteId, query.DocumentosPermitidos);
        var envios = await db.EstadoCuentaEnvios.AsNoTracking()
            .Where(e => e.ClienteId == query.ClienteId)
            .OrderByDescending(e => e.Fecha).ThenByDescending(e => e.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
        return envios.Select(EstadoCuentaEnvioMapper.ToResponse).ToList();
    }
}
