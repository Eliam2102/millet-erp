using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.CartaPorte.CorregirCartaPorte;

/// <summary>
/// M-354: corrige una Carta Porte observada (timbrado fallido) con rastro, antes de reemitirla con «Reintentar». Corrige los
/// datos del tramo que se informen y, con <see cref="ActualizarEmisor"/>, el emisor desde los datos fiscales vigentes de la
/// empresa y la sucursal. El motivo es obligatorio y cada campo cambiado queda en la bitácora.
/// </summary>
public sealed record CorregirCartaPorteCommand(
    Guid CartaPorteId,
    string Motivo,
    bool ActualizarEmisor = false,
    string? Origen = null,
    string? Destino = null,
    decimal? DistanciaKm = null,
    string? OrigenCodigoPostal = null,
    string? OrigenEstado = null,
    string? DestinoCodigoPostal = null,
    string? DestinoEstado = null,
    Guid? VehiculoId = null,
    Guid? OperadorId = null,
    DateTimeOffset? FechaSalida = null,
    DateTimeOffset? FechaLlegadaEstimada = null) : IRequest<IReadOnlyList<CorreccionTramoResponse>>;

public sealed record CorreccionTramoResponse(string Campo, string? ValorAnterior, string? ValorNuevo, string Motivo, string? Usuario, DateTimeOffset En);

public sealed class CorregirCartaPorteValidator : AbstractValidator<CorregirCartaPorteCommand>
{
    public CorregirCartaPorteValidator()
    {
        RuleFor(c => c.CartaPorteId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MinimumLength(5).MaximumLength(500);
        RuleFor(c => c.DistanciaKm).GreaterThanOrEqualTo(0).When(c => c.DistanciaKm.HasValue);
        RuleFor(c => c.OrigenCodigoPostal).MaximumLength(5);
        RuleFor(c => c.DestinoCodigoPostal).MaximumLength(5);
    }
}

public sealed class CorregirCartaPorteHandler(
    FacturacionDbContext db, IEmpresaFiscalReadPort empresasFiscal, ICurrentEmpresaContext empresa,
    ICurrentUserContext usuario, IClock clock) : IRequestHandler<CorregirCartaPorteCommand, IReadOnlyList<CorreccionTramoResponse>>
{
    public async Task<IReadOnlyList<CorreccionTramoResponse>> Handle(CorregirCartaPorteCommand command, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        var cp = await db.CartasPorte.FirstOrDefaultAsync(c => c.Id == command.CartaPorteId, cancellationToken)
            ?? throw new EntityNotFoundException("CARTA_PORTE_NO_ENCONTRADA", $"No existe la Carta Porte '{command.CartaPorteId}'.");

        // Nombres legibles de vehículo y operador para el rastro.
        var ids = new[] { cp.VehiculoId, command.VehiculoId ?? Guid.Empty };
        var vehiculos = await db.Vehiculos.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Placa, cancellationToken);
        var opIds = new[] { cp.OperadorId, command.OperadorId ?? Guid.Empty };
        var operadores = await db.Operadores.Where(o => opIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Nombre, cancellationToken);
        if (command.VehiculoId is Guid v && v != Guid.Empty && !vehiculos.ContainsKey(v))
            throw new EntityNotFoundException("VEHICULO_NO_ENCONTRADO", $"No existe el vehículo {v}.");
        if (command.OperadorId is Guid o && o != Guid.Empty && !operadores.ContainsKey(o))
            throw new EntityNotFoundException("OPERADOR_NO_ENCONTRADO", $"No existe el operador {o}.");

        var cambios = cp.CorregirDatosTramo(
            new Domain.CartaPorte.CartaPorte.CorreccionDatos(command.Origen, command.Destino, command.DistanciaKm,
                command.OrigenCodigoPostal, command.OrigenEstado, command.DestinoCodigoPostal, command.DestinoEstado,
                command.VehiculoId, command.OperadorId, command.FechaSalida, command.FechaLlegadaEstimada),
            vehiculos.GetValueOrDefault(cp.VehiculoId), command.VehiculoId is Guid vn ? vehiculos.GetValueOrDefault(vn) : null,
            operadores.GetValueOrDefault(cp.OperadorId), command.OperadorId is Guid on ? operadores.GetValueOrDefault(on) : null).ToList();

        if (command.ActualizarEmisor)
        {
            var nuevo = await empresasFiscal.ObtenerEmisorAsync(empresaId, cp.SucursalId, cancellationToken)
                ?? throw new BusinessRuleException("EMISOR_NO_DISPONIBLE", "Revisa los datos fiscales de la empresa y que la sucursal emisora esté activa.");
            var antes = cp.SnapshotEmisor();
            cp.ActualizarEmisorParaReintento(nuevo);
            void Emisor(string campo, string a, string n)
            {
                if (!string.Equals(a, n, StringComparison.Ordinal)) cambios.Add(new CambioTramo(campo, a, n));
            }
            Emisor("emisor.rfc", antes.Rfc, cp.RfcEmisor);
            Emisor("emisor.nombre", antes.Nombre, cp.NombreEmisor);
            Emisor("emisor.regimenFiscal", antes.RegimenFiscal, cp.RegimenFiscalEmisor);
            Emisor("emisor.lugarExpedicion", antes.LugarExpedicion, cp.LugarExpedicion);
        }

        if (cambios.Count == 0)
            throw new BusinessRuleException("CARTA_PORTE_CORRECCION_SIN_CAMBIOS", "No hay nada que corregir: los datos informados ya son los de la Carta Porte.");

        var tramoId = await db.TramosTraslado.Where(t => t.CartaPorteId == cp.Id).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
        var ahora = clock.UtcNow;
        foreach (var c in cambios)
            db.CorreccionesTramo.Add(new CorreccionTramo(empresaId, cp.Id, tramoId, c, command.Motivo.Trim(), usuario.UserId, usuario.UserName, ahora));
        await db.SaveChangesAsync(cancellationToken);
        return cambios.Select(c => new CorreccionTramoResponse(c.Campo, c.Anterior, c.Nuevo, command.Motivo.Trim(), usuario.UserName, ahora)).ToList();
    }
}

/// <summary>Bitácora de correcciones de una Carta Porte, la más reciente primero.</summary>
public sealed record CorreccionesCartaPorteQuery(Guid CartaPorteId) : IRequest<IReadOnlyList<CorreccionTramoResponse>>;

public sealed class CorreccionesCartaPorteHandler(FacturacionDbContext db)
    : IRequestHandler<CorreccionesCartaPorteQuery, IReadOnlyList<CorreccionTramoResponse>>
{
    public async Task<IReadOnlyList<CorreccionTramoResponse>> Handle(CorreccionesCartaPorteQuery query, CancellationToken cancellationToken) =>
        (await db.CorreccionesTramo.AsNoTracking().Where(c => c.CartaPorteId == query.CartaPorteId)
            .OrderByDescending(c => c.En).ToListAsync(cancellationToken))
        .Select(c => new CorreccionTramoResponse(c.Campo, c.ValorAnterior, c.ValorNuevo, c.Motivo, c.UsuarioNombre, c.En)).ToList();
}
