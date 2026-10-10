using MediatR;
using Millet.Administracion.Application.Abstractions;
using Millet.Api.Web;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests;

public sealed class SucursalScopeP6Tests
{
    private static readonly Guid Cancun = Guid.NewGuid();
    private static readonly Guid Circuito = Guid.NewGuid();
    [Fact]
    public async Task Documento_mixto_exige_todas_las_sucursales_y_documento_sin_origen_exige_corporativo()
    {
        var puerto = new Puerto([new(Guid.NewGuid(), [Cancun, Circuito]), new(Guid.NewGuid(), [])]);
        var scope = Scope(new Permisos(false), puerto);
        foreach (var documento in puerto.Documentos)
            await Assert.ThrowsAsync<ForbiddenException>(() => scope.VerificarAsync("factura_proveedor", documento.Id, "corporativo", CancellationToken.None));
        var corporativo = Scope(new Permisos(true), puerto);
        foreach (var documento in puerto.Documentos)
            await corporativo.VerificarAsync("factura_proveedor", documento.Id, "corporativo", CancellationToken.None);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pipeline_filtra_antes_del_handler_y_corporativo_no_restringe(bool corporativo)
    {
        var propia = Guid.NewGuid(); var ajena = Guid.NewGuid(); var mixta = Guid.NewGuid();
        var puerto = new Puerto([new(propia, [Cancun]), new(ajena, [Circuito]), new(mixta, [Cancun, Circuito])]);
        var permisos = new Permisos(corporativo);
        var query = new Consulta();
        var behavior = new SucursalScopeQueryBehavior<Consulta, bool>(new Usuario(), permisos, new Sucursales(), Scope(permisos, puerto));
        await behavior.Handle(query, () => {
            if (corporativo) { Assert.Null(query.SucursalesPermitidas); Assert.Null(query.DocumentosPermitidos); }
            else { Assert.Equal(new[] { Cancun }, query.SucursalesPermitidas); Assert.Equal(new[] { propia }, query.DocumentosPermitidos); }
            return Task.FromResult(true);
        }, CancellationToken.None);
    }
    private static DocumentoSucursalScope Scope(Permisos permisos, Puerto puerto)
        => new(new Usuario(), permisos, new Sucursales(), puerto, puerto, puerto, puerto, puerto, puerto);
    private sealed record Consulta : IRequest<bool>, IDocumentoScopedQuery
    {
        public string PermisoTodasSucursales => "corporativo";
        public string TipoDocumento => "factura_proveedor";
        public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
        public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
    }
    private sealed class Usuario : ICurrentUserContext
    { public Guid? UserId => Guid.Parse("00000006-0000-0000-0000-000000000001"); public string? UserName => "P6"; }
    private sealed class Permisos(bool corporativo) : ICurrentUserPermissions
    { public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(corporativo); }
    private sealed class Sucursales : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([Cancun]);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken cancellationToken) => Task.FromResult(sucursalId == Cancun);
    }
    private sealed class Puerto(IReadOnlyList<DocumentoSucursales> documentos) : IComprasSucursalReadPort, IFacturacionSucursalReadPort, ICxpSucursalReadPort, ICxcSucursalReadPort, ITesoreriaSucursalReadPort, IAlmacenSucursalReadPort
    {
        public IReadOnlyList<DocumentoSucursales> Documentos => documentos;
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct) => Task.FromResult(documentos);
    }
}
