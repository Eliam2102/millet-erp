using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.UnitTests.Adjuntos;

public sealed class ExpedienteProveedorReadAdapterTests
{
    private static readonly DateTimeOffset Subido = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(AdjuntosEntorno E, Guid ProveedorId)> PrepararAsync(TipoPersonaProveedor tipo)
    {
        var e = new AdjuntosEntorno();
        await Siembra.TiposProveedorAsync(e.Db);
        var p = new Proveedor(Guid.NewGuid(), "P001", "Vidrios SA", "VSA010101AAA", tipo);
        e.Db.Proveedores.Add(p);
        await e.Db.SaveChangesAsync();
        return (e, p.Id);
    }

    private static async Task AdjuntarAsync(
        AdjuntosEntorno e, Guid proveedorId, Guid tipoId, DateOnly? vigenteHasta, DateTimeOffset? subidoEn = null)
    {
        e.Db.Adjuntos.Add(new Adjunto(
            Guid.NewGuid(), "proveedor", proveedorId, null, tipoId, "doc.pdf", "application/pdf", 10,
            new string('a', 64), $"proveedor/{proveedorId}/{Guid.NewGuid()}.pdf", vigenteHasta, Guid.NewGuid(),
            subidoEn ?? Subido));
        await e.Db.SaveChangesAsync();
    }

    private static ExpedienteProveedorReadAdapter Adaptador(AdjuntosEntorno e) => new(e.Db, e.Clock);

    [Fact]
    public async Task Proveedor_inexistente_devuelve_null()
    {
        using var e = new AdjuntosEntorno();
        (await Adaptador(e).ObtenerAsync(Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact]
    public async Task Sin_adjuntos_todo_son_faltantes()
    {
        var (e, id) = await PrepararAsync(TipoPersonaProveedor.Moral);
        using var _ = e;

        var r = await Adaptador(e).ObtenerAsync(id, default);

        r!.Completo.Should().BeFalse();
        r.Faltantes.Should().HaveCount(5);
    }

    [Fact]
    public async Task Cinco_documentos_vigentes_de_persona_moral_es_completo()
    {
        var (e, id) = await PrepararAsync(TipoPersonaProveedor.Moral);
        using var _ = e;
        var lejos = new DateOnly(2027, 6, 1);
        foreach (var t in new[] { Siembra.TipoCsf, Siembra.TipoContrato, Siembra.TipoActa, Siembra.TipoIdentificacion, Siembra.TipoDomicilio })
            await AdjuntarAsync(e, id, t, lejos);

        var r = await Adaptador(e).ObtenerAsync(id, default);

        r!.Completo.Should().BeTrue();
        r.Faltantes.Should().BeEmpty();
        r.Vencidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Persona_fisica_no_requiere_acta_constitutiva()
    {
        var (e, id) = await PrepararAsync(TipoPersonaProveedor.Fisica);
        using var _ = e;
        foreach (var t in new[] { Siembra.TipoCsf, Siembra.TipoContrato, Siembra.TipoIdentificacion, Siembra.TipoDomicilio })
            await AdjuntarAsync(e, id, t, new DateOnly(2027, 6, 1));

        var r = await Adaptador(e).ObtenerAsync(id, default);

        r!.Completo.Should().BeTrue();
        r.Faltantes.Should().NotContain("acta_constitutiva");
    }

    [Fact]
    public async Task Documento_vencido_hace_incompleto_el_expediente()
    {
        var (e, id) = await PrepararAsync(TipoPersonaProveedor.Fisica);
        using var _ = e;
        // Hoy (México) = 2026-10-31; vigente "al 30-oct" ya venció.
        await AdjuntarAsync(e, id, Siembra.TipoCsf, new DateOnly(2026, 10, 30));
        foreach (var t in new[] { Siembra.TipoContrato, Siembra.TipoIdentificacion, Siembra.TipoDomicilio })
            await AdjuntarAsync(e, id, t, new DateOnly(2027, 6, 1));

        var r = await Adaptador(e).ObtenerAsync(id, default);

        r!.Completo.Should().BeFalse();
        r.Vencidos.Should().Equal("constancia_situacion_fiscal");
    }

    [Fact]
    public async Task Por_vencer_cuenta_como_completo_y_se_reporta()
    {
        var (e, id) = await PrepararAsync(TipoPersonaProveedor.Fisica);
        using var _ = e;
        await AdjuntarAsync(e, id, Siembra.TipoCsf, new DateOnly(2026, 11, 15));
        foreach (var t in new[] { Siembra.TipoContrato, Siembra.TipoIdentificacion, Siembra.TipoDomicilio })
            await AdjuntarAsync(e, id, t, new DateOnly(2027, 6, 1));

        var r = await Adaptador(e).ObtenerAsync(id, default);

        r!.Completo.Should().BeTrue();
        r.PorVencer.Should().Equal("constancia_situacion_fiscal");
    }

    [Fact]
    public async Task Baja_deja_el_tipo_faltante_y_manda_el_mas_reciente()
    {
        var (e, id) = await PrepararAsync(TipoPersonaProveedor.Fisica);
        using var _ = e;
        // Más viejo vigente + más reciente vencido => el actual (más reciente) es el vencido.
        await AdjuntarAsync(e, id, Siembra.TipoCsf, new DateOnly(2027, 6, 1), Subido);
        await AdjuntarAsync(e, id, Siembra.TipoCsf, new DateOnly(2026, 1, 1), Subido.AddDays(5));
        // Contrato dado de baja => faltante.
        await AdjuntarAsync(e, id, Siembra.TipoContrato, null);
        var contrato = e.Db.Adjuntos.Single(a => a.TipoDocumentoId == Siembra.TipoContrato);
        contrato.DarDeBaja("Documento equivocado", Guid.NewGuid(), Subido.AddDays(6));
        await e.Db.SaveChangesAsync();

        var r = await Adaptador(e).ObtenerAsync(id, default);

        r!.Vencidos.Should().Equal("constancia_situacion_fiscal");
        r.Faltantes.Should().Contain("contrato");
    }
}
