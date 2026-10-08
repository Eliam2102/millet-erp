using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// PATCH parcial sobre un proveedor (B.5). Convención: nullable null
/// = no tocar, flag <c>limpiarX</c> = setear nullable a null. Cambios
/// permitidos: razonSocial, nombreComercial, rfc, tipoPersona,
/// condicionesPago, moneda preferida, email, telefono y datos bancarios
/// (banco/clabe/beneficiario — TES-PR3 [T-G1]). Si <c>rfc</c> viene y
/// cambia, único (excepto genéricos SAT, F1-ADM-05) → 409
/// <c>PROVEEDOR_RFC_DUPLICADO</c>.
/// Inmutables: id, clave, claveLegacy.
/// </summary>
public sealed record ActualizarProveedorCommand(
    Guid ProveedorId,
    string? RazonSocial = null,
    string? NombreComercial = null,
    string? Rfc = null,
    TipoPersonaProveedor? TipoPersona = null,
    short? CondicionesPagoDias = null,
    Guid? MonedaPreferidaId = null,
    string? Email = null,
    string? Telefono = null,
    bool LimpiarNombreComercial = false,
    bool LimpiarCondicionesPago = false,
    bool LimpiarMonedaPreferida = false,
    bool LimpiarEmail = false,
    bool LimpiarTelefono = false,
    string? Banco = null,
    string? Clabe = null,
    string? Beneficiario = null,
    bool LimpiarBanco = false,
    bool LimpiarClabe = false,
    bool LimpiarBeneficiario = false) : IRequest;

public sealed class ActualizarProveedorValidator : AbstractValidator<ActualizarProveedorCommand>
{
    public ActualizarProveedorValidator()
    {
        RuleFor(c => c.ProveedorId).NotEqual(Guid.Empty);
        RuleFor(c => c.RazonSocial!).NotEmpty().MaximumLength(254)
            .When(c => c.RazonSocial is not null);
        RuleFor(c => c.NombreComercial!).MaximumLength(254)
            .When(c => c.NombreComercial is not null);
        RuleFor(c => c.Rfc!).Length(12, 13).When(c => c.Rfc is not null);
        RuleFor(c => c.TipoPersona).IsInEnum().When(c => c.TipoPersona.HasValue);
        RuleFor(c => c.CondicionesPagoDias).InclusiveBetween((short)0, (short)365)
            .When(c => c.CondicionesPagoDias.HasValue);
        RuleFor(c => c.Email!).EmailAddress().MaximumLength(254)
            .When(c => !string.IsNullOrEmpty(c.Email));
        RuleFor(c => c.Telefono!).MaximumLength(50).When(c => c.Telefono is not null);
        RuleFor(c => c.Banco!).NotEmpty().MaximumLength(120).When(c => c.Banco is not null);
        RuleFor(c => c.Clabe!).Length(18).Matches(@"^\d{18}$")
            .WithMessage("La CLABE debe tener 18 dígitos.")
            .When(c => c.Clabe is not null);
        RuleFor(c => c.Beneficiario!).NotEmpty().MaximumLength(254)
            .When(c => c.Beneficiario is not null);
    }
}

public sealed class ActualizarProveedorHandler : IRequestHandler<ActualizarProveedorCommand>
{
    // Compartido NO referencia el proyecto Identidad (Identidad ya
    // referencia Compartido; evita el ciclo) — mismo patrón que
    // SucursalScopeGuardPermisos: literal duplicado en sync con
    // Millet.Identidad.Domain.PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar.
    private const string PermisoBancariosEditar = "datos_maestros.proveedores.bancarios-editar";

    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserPermissions _permissions;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IAuditLogWriter _audit;

    public ActualizarProveedorHandler(
        CompartidoDbContext db,
        ICurrentUserPermissions permissions,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext empresa,
        IAuditLogWriter audit)
    {
        _db = db;
        _permissions = permissions;
        _currentUser = currentUser;
        _empresa = empresa;
        _audit = audit;
    }

    public async Task Handle(ActualizarProveedorCommand request, CancellationToken cancellationToken)
    {
        // F1-ADM-05: banco/CLABE/beneficiario requieren el permiso dedicado
        // de edición de bancarios, separado del permiso grueso de
        // catálogos que ya protege el endpoint — chequeo antes de tocar
        // nada más del request.
        var tocaBancarios = request.Banco is not null
            || request.Clabe is not null
            || request.Beneficiario is not null
            || request.LimpiarBanco
            || request.LimpiarClabe
            || request.LimpiarBeneficiario;
        if (tocaBancarios
            && !await _permissions.TieneAsync(PermisoBancariosEditar, cancellationToken))
        {
            throw new ForbiddenException(
                "PROVEEDOR_BANCARIOS_SIN_PERMISO",
                "No tiene permiso para cambiar los datos bancarios del proveedor.");
        }

        var proveedor = await _db.Proveedores
            .FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{request.ProveedorId}'.");

        // Cross-table: si llega un id de moneda, validar antes de mutar.
        if (request.MonedaPreferidaId is Guid monedaId)
        {
            var existeMoneda = await _db.Monedas.AsNoTracking()
                .AnyAsync(m => m.Id == monedaId, cancellationToken);
            if (!existeMoneda)
            {
                throw new EntityNotFoundException(
                    "MONEDA_NO_ENCONTRADA",
                    $"No existe moneda con id '{monedaId}' en compartido.monedas.");
            }
        }

        // RFC único (F1-ADM-05), excepto genéricos SAT: solo se valida si
        // el RFC viene en el body y cambia respecto al actual. Genérico en
        // edición se permite sin más (no hay razón social "nueva" que
        // comparar contra un alta ya existente).
        if (request.Rfc is not null)
        {
            var rfcNormalizado = request.Rfc.Trim().ToUpperInvariant();
            if (rfcNormalizado != proveedor.Rfc && !Proveedor.EsRfcGenerico(rfcNormalizado))
            {
                var duplicadoRfc = await _db.Proveedores.AsNoTracking()
                    .FirstOrDefaultAsync(
                        p => p.Rfc == rfcNormalizado && p.Id != proveedor.Id, cancellationToken);
                if (duplicadoRfc is not null)
                {
                    throw new ConflictException(
                        "PROVEEDOR_RFC_DUPLICADO",
                        $"Ya existe el proveedor {duplicadoRfc.Clave} · {duplicadoRfc.RazonSocial} " +
                        $"con RFC {rfcNormalizado}.");
                }
            }
        }

        var bancoAntes = proveedor.Banco;
        var clabeAntes = proveedor.Clabe;
        var beneficiarioAntes = proveedor.Beneficiario;

        proveedor.ActualizarDatos(
            razonSocial: request.RazonSocial,
            nombreComercial: request.NombreComercial,
            rfc: request.Rfc,
            tipoPersona: request.TipoPersona,
            condicionesPagoDias: request.CondicionesPagoDias,
            monedaPreferidaId: request.MonedaPreferidaId,
            email: request.Email,
            telefono: request.Telefono,
            banco: request.Banco,
            clabe: request.Clabe,
            beneficiario: request.Beneficiario,
            limpiarNombreComercial: request.LimpiarNombreComercial,
            limpiarCondicionesPago: request.LimpiarCondicionesPago,
            limpiarMonedaPreferida: request.LimpiarMonedaPreferida,
            limpiarEmail: request.LimpiarEmail,
            limpiarTelefono: request.LimpiarTelefono,
            limpiarBanco: request.LimpiarBanco,
            limpiarClabe: request.LimpiarClabe,
            limpiarBeneficiario: request.LimpiarBeneficiario);

        await _db.SaveChangesAsync(cancellationToken);

        // G1.9 / F1-ADM-05: si cambiaron datos bancarios, auditar evento explícito
        // con CLABE anterior/nueva enmascaradas (nunca la CLABE completa).
        var cambioBanco = bancoAntes != proveedor.Banco;
        var cambioClabe = clabeAntes != proveedor.Clabe;
        var cambioBeneficiario = beneficiarioAntes != proveedor.Beneficiario;

        if (cambioBanco || cambioClabe || cambioBeneficiario)
        {
            await _audit.RegistrarAsync(
                operacion: "proveedor.bancarios-cambiados",
                modulo: "DatosMaestros",
                entidad: "Proveedor",
                entidadId: proveedor.Id,
                aggregateRootId: proveedor.Id,
                actorNombre: _currentUser.UserName ?? "Usuario",
                actorTipo: "usuario",
                actorEmail: _currentUser.Email,
                entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
                resumen: $"Datos bancarios del proveedor {proveedor.Clave} modificados (CLABE: {Mascara(clabeAntes) ?? "sin-asignar"} → {Mascara(proveedor.Clabe) ?? "sin-asignar"}).",
                usuarioId: _currentUser.UserId,
                empresaId: _empresa.Current,
                cambios: JsonSerializer.Serialize(new
                {
                    banco = new { antes = bancoAntes, despues = proveedor.Banco },
                    clabe = new { antes = Mascara(clabeAntes), despues = Mascara(proveedor.Clabe) },
                    beneficiario = new { antes = beneficiarioAntes, despues = proveedor.Beneficiario },
                }),
                cancellationToken: cancellationToken);
        }
    }

    private static string? Mascara(string? v)
    {
        if (v is null) return null;
        var s = v.Trim();
        return s.Length <= 4
            ? new string('*', s.Length)
            : string.Concat(new string('*', s.Length - 4), s.AsSpan(s.Length - 4));
    }
}
