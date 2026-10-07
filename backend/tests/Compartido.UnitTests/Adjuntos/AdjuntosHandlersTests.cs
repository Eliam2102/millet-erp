using System.Security.Cryptography;
using FluentValidation.TestHelper;
using MediatR;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Auditoria;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Adjuntos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.UnitTests.Adjuntos;

public sealed class AdjuntosHandlersTests
{
    private const string Ver = "datos_maestros.proveedores.adjuntos-ver";
    private const string SubirP = "datos_maestros.proveedores.adjuntos-subir";
    private const string BajaP = "datos_maestros.proveedores.adjuntos-baja";

    private static readonly byte[] Pdf = [.. "%PDF-1.4 contenido de prueba"u8];

    // ---------------------------------------------------------------- entorno

    private static async Task<(AdjuntosEntorno E, Guid ProveedorId)> PrepararAsync(
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        TipoPersonaProveedor tipo = TipoPersonaProveedor.Moral,
        params string[] permisos)
    {
        var e = new AdjuntosEntorno();
        await Siembra.TiposProveedorAsync(e.Db);
        var p = new Proveedor(Guid.NewGuid(), "P001", "Vidrios SA", "VSA010101AAA", tipo, estatus);
        e.Db.Proveedores.Add(p);
        await e.Db.SaveChangesAsync();
        e.Conceder(permisos.Length > 0 ? permisos : [Ver, SubirP, BajaP]);
        return (e, p.Id);
    }

    private static SubirAdjuntoHandler Subidor(AdjuntosEntorno e) =>
        new(e.Acceso, e.Db, e.Blob, Options.Create(new AdjuntosPoliticaOptions()), e.Clock);

    private static SubirAdjuntoCommand Subir(
        Guid proveedorId, Guid tipoId, byte[]? bytes = null, string nombre = "doc.pdf",
        string contentType = "application/pdf", DateOnly? vigencia = null, string tipoEntidad = "proveedor")
        => new(tipoEntidad, proveedorId, tipoId, nombre, contentType, new MemoryStream(bytes ?? Pdf), vigencia);

    private static async Task<AdjuntoResponse> SubirOkAsync(
        AdjuntosEntorno e, Guid proveedorId, Guid tipoId, DateOnly? vigencia = null)
        => await Subidor(e).Handle(Subir(proveedorId, tipoId, vigencia: vigencia), default);

    private static async Task<Guid> OtroProveedorAsync(AdjuntosEntorno e)
    {
        var p = new Proveedor(Guid.NewGuid(), "P002", "Otro SA", "OTR010101AAA", TipoPersonaProveedor.Moral);
        e.Db.Proveedores.Add(p);
        await e.Db.SaveChangesAsync();
        return p.Id;
    }

    // ------------------------------------------------------------------ subir

    [Fact]
    public async Task Subir_ok_guarda_blob_y_fila_con_hash_y_tamano()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;

        var r = await SubirOkAsync(e, id, Siembra.TipoContrato);

        r.HashSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Pdf)));
        r.TamanoBytes.Should().Be(Pdf.Length);
        r.SubidoPorId.Should().Be(e.Usuario.UserId!.Value);
        e.Blob.Blobs.Should().ContainSingle().Which.Value.Should().Equal(Pdf);
        e.Blob.Blobs.Keys.Single().Should().StartWith($"proveedor/{id}/").And.EndWith(".pdf");
        e.Db.Adjuntos.Should().ContainSingle();
    }

    [Fact]
    public async Task Subir_sin_permiso_es_403_sin_blob_ni_fila()
    {
        var (e, id) = await PrepararAsync(permisos: Ver);
        using var _ = e;

        var act = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        e.Blob.Blobs.Should().BeEmpty();
        e.Db.Adjuntos.Should().BeEmpty();
    }

    [Fact]
    public async Task Subir_a_proveedor_inexistente_es_404()
    {
        var (e, _) = await PrepararAsync();
        using var _e = e;

        var act = () => Subidor(e).Handle(Subir(Guid.NewGuid(), Siembra.TipoContrato), default);

        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Code.Should().Be("PROVEEDOR_NO_ENCONTRADO");
        e.Blob.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Subir_a_proveedor_inactivo_no_se_admite()
    {
        var (e, id) = await PrepararAsync(EstatusCatalogo.Inactivo);
        using var _ = e;

        var act = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ADJUNTO_ENTIDAD_NO_ADMITE_SUBIDA");
        e.Blob.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Subir_tipo_de_entidad_desconocido_es_404()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;

        var act = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato, tipoEntidad: "poliza"), default);

        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Code.Should().Be("ADJUNTO_TIPO_ENTIDAD_DESCONOCIDO");
    }

    [Fact]
    public async Task Subir_tipo_de_documento_inexistente_es_404()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;

        var act = () => Subidor(e).Handle(Subir(id, Guid.NewGuid()), default);

        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Code.Should().Be("ADJUNTO_TIPO_DOCUMENTO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Subir_acta_de_persona_fisica_no_aplica()
    {
        var (e, id) = await PrepararAsync(tipo: TipoPersonaProveedor.Fisica);
        using var _ = e;

        var act = () => Subidor(e).Handle(Subir(id, Siembra.TipoActa), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ADJUNTO_TIPO_NO_APLICA");
    }

    [Fact]
    public async Task Vigencia_por_defecto_es_hoy_mas_meses_del_tipo_en_fecha_de_mexico()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        // 01-nov 03:00 UTC = 31-oct 21:00 en México: "hoy" es 31-oct.
        e.Clock.UtcNow = new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);

        var csf = await SubirOkAsync(e, id, Siembra.TipoCsf);       // 3 meses
        var contrato = await SubirOkAsync(e, id, Siembra.TipoContrato); // sin vigencia

        csf.VigenteHasta.Should().Be(new DateOnly(2027, 1, 31));
        contrato.VigenteHasta.Should().BeNull();
        contrato.Estado.Should().Be(EstadoAdjunto.SinVigencia);
    }

    [Fact]
    public async Task Vigencia_explicita_de_hoy_se_acepta_y_la_pasada_se_rechaza()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var hoy = new DateOnly(2026, 10, 31);

        var ok = await SubirOkAsync(e, id, Siembra.TipoContrato, hoy);
        ok.VigenteHasta.Should().Be(hoy);
        ok.Estado.Should().Be(EstadoAdjunto.PorVencer);

        var bloqueos = e.Blob.Blobs.Count;
        var act = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato, vigencia: hoy.AddDays(-1)), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ADJUNTO_VIGENCIA_PASADA");
        e.Blob.Blobs.Should().HaveCount(bloqueos);
    }

    [Fact]
    public async Task Vigente_al_31_oct_aparece_vencida_el_1_nov()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var subido = await SubirOkAsync(e, id, Siembra.TipoContrato, new DateOnly(2026, 10, 31));
        var obtener = new ObtenerAdjuntoHandler(e.Acceso, e.Db, e.Clock);

        (await obtener.Handle(new("proveedor", id, subido.Id), default)).Estado.Should().Be(EstadoAdjunto.PorVencer);

        e.Clock.UtcNow = new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);
        (await obtener.Handle(new("proveedor", id, subido.Id), default)).Estado.Should().Be(EstadoAdjunto.Vencido);
    }

    [Theory]
    [InlineData("malware.exe", "application/octet-stream")]
    [InlineData("doc.pdf", "image/png")]
    public async Task Subir_formato_no_permitido_no_toca_blob_ni_bd(string nombre, string contentType)
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;

        var act = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato, nombre: nombre, contentType: contentType), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ADJUNTO_FORMATO_NO_PERMITIDO");
        e.Blob.Blobs.Should().BeEmpty();
        e.Db.Adjuntos.Should().BeEmpty();
    }

    [Fact]
    public async Task Subir_pdf_sin_firma_y_archivo_vacio_se_rechazan()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;

        var sinFirma = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato, bytes: [.. "no soy pdf"u8]), default);
        (await sinFirma.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ADJUNTO_FORMATO_NO_PERMITIDO");

        var vacio = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato, bytes: []), default);
        (await vacio.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ADJUNTO_ARCHIVO_VACIO");

        e.Blob.Blobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Compensa_el_blob_si_falla_la_bd()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        e.FallaAlGuardar.Activo = true;

        var act = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("BD caída");
        e.Blob.Blobs.Should().BeEmpty("el blob subido se elimina al fallar la persistencia");
    }

    [Fact]
    public async Task Compensa_tambien_si_el_blob_falla_a_medias()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        e.Blob.FallarAlSubir = true;

        var act = () => Subidor(e).Handle(Subir(id, Siembra.TipoContrato), default);

        await act.Should().ThrowAsync<IOException>();
        e.Blob.Blobs.Should().BeEmpty();
        e.Db.Adjuntos.Should().BeEmpty();
    }

    [Fact]
    public void Validador_de_subida_exige_campos_y_stream_posicionable()
    {
        var v = new SubirAdjuntoValidator();
        v.TestValidate(new SubirAdjuntoCommand("", Guid.Empty, Guid.Empty, "", "", new MemoryStream()))
            .ShouldHaveValidationErrorFor(x => x.TipoEntidad);
        v.TestValidate(new SubirAdjuntoCommand("proveedor", Guid.NewGuid(), Guid.NewGuid(), "a.pdf", "application/pdf", new SinSeek()))
            .ShouldHaveValidationErrorFor(x => x.Contenido);
    }

    // ------------------------------------------------------------- listar/ver

    [Fact]
    public async Task Listar_sin_permiso_de_ver_es_403()
    {
        var (e, id) = await PrepararAsync(permisos: SubirP);
        using var _ = e;

        var act = () => new ListarAdjuntosHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Listar_oculta_bajas_y_solo_las_muestra_con_permiso_de_baja()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var a1 = await SubirOkAsync(e, id, Siembra.TipoContrato);
        await SubirOkAsync(e, id, Siembra.TipoIdentificacion);
        await new DarDeBajaAdjuntoHandler(e.Acceso, e.Db, e.Clock)
            .Handle(new("proveedor", id, a1.Id, "Documento equivocado"), default);
        var listar = new ListarAdjuntosHandler(e.Acceso, e.Db, e.Clock);

        (await listar.Handle(new("proveedor", id), default)).Should().ContainSingle();
        (await listar.Handle(new("proveedor", id, IncluirBajas: true), default)).Should().HaveCount(2);

        e.Permisos.Concedidos.Remove(BajaP);
        var act = () => listar.Handle(new("proveedor", id, IncluirBajas: true), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Adjunto_de_otro_proveedor_es_404_en_todas_las_operaciones_IDOR()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var otro = await OtroProveedorAsync(e);
        var ajeno = await SubirOkAsync(e, otro, Siembra.TipoContrato);

        Func<Task>[] intentos =
        [
            () => new ObtenerAdjuntoHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id, ajeno.Id), default),
            () => new DarDeBajaAdjuntoHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id, ajeno.Id, "Motivo valido"), default),
            () => new EmitirEnlaceDescargaHandler(e.Acceso, e.Db, e.Enlaces).Handle(new("proveedor", id, ajeno.Id), default),
            () => new ObtenerContenidoAdjuntoHandler(e.Acceso, e.Db, e.Blob).Handle(new("proveedor", id, ajeno.Id), default),
            () => new ConsultarBitacoraAdjuntoHandler(e.Acceso, e.Db, new CapturaMediator(), e.Clock).Handle(new("proveedor", id, ajeno.Id), default),
        ];
        foreach (var intento in intentos)
        {
            (await FluentActions.Awaiting(intento).Should().ThrowAsync<EntityNotFoundException>())
                .Which.Code.Should().Be("ADJUNTO_NO_ENCONTRADO");
        }
        e.Db.Adjuntos.Single(a => a.Id == ajeno.Id).EstaDeBaja.Should().BeFalse();
    }

    [Fact]
    public async Task Adjunto_dado_de_baja_solo_es_visible_con_permiso_de_baja()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var a = await SubirOkAsync(e, id, Siembra.TipoContrato);
        await new DarDeBajaAdjuntoHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id, a.Id, "Documento equivocado"), default);
        var obtener = new ObtenerAdjuntoHandler(e.Acceso, e.Db, e.Clock);

        (await obtener.Handle(new("proveedor", id, a.Id), default)).Estado.Should().Be(EstadoAdjunto.Baja);

        e.Permisos.Concedidos.Remove(BajaP);
        var act = () => obtener.Handle(new("proveedor", id, a.Id), default);
        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Code.Should().Be("ADJUNTO_NO_ENCONTRADO");
    }

    // ------------------------------------------------------------------- baja

    [Fact]
    public async Task Baja_registra_motivo_quien_y_cuando_y_conserva_el_blob()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var a = await SubirOkAsync(e, id, Siembra.TipoContrato);
        e.Clock.UtcNow = e.Clock.UtcNow.AddHours(2);

        var r = await new DarDeBajaAdjuntoHandler(e.Acceso, e.Db, e.Clock)
            .Handle(new("proveedor", id, a.Id, "  Documento equivocado  "), default);

        r.Estado.Should().Be(EstadoAdjunto.Baja);
        r.BajaMotivo.Should().Be("Documento equivocado");
        r.BajaPorId.Should().Be(e.Usuario.UserId!.Value);
        r.BajaEn.Should().Be(e.Clock.UtcNow);
        e.Blob.Blobs.Should().ContainSingle("el blob jamás se borra en baja lógica");
    }

    [Fact]
    public async Task Baja_doble_es_error()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var a = await SubirOkAsync(e, id, Siembra.TipoContrato);
        var baja = new DarDeBajaAdjuntoHandler(e.Acceso, e.Db, e.Clock);
        await baja.Handle(new("proveedor", id, a.Id, "Primera baja"), default);

        var act = () => baja.Handle(new("proveedor", id, a.Id, "Segunda baja"), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ADJUNTO_YA_DADO_DE_BAJA");
        e.Blob.Blobs.Should().ContainSingle();
    }

    [Fact]
    public async Task Baja_sin_permiso_es_403_y_no_cambia_nada()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var a = await SubirOkAsync(e, id, Siembra.TipoContrato);
        e.Permisos.Concedidos.Remove(BajaP);

        var act = () => new DarDeBajaAdjuntoHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id, a.Id, "Motivo valido"), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        e.Db.Adjuntos.Single().EstaDeBaja.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("corto")]
    [InlineData("   ab   ")]
    public void Validador_de_baja_exige_motivo_de_5_a_500(string motivo)
    {
        var v = new DarDeBajaAdjuntoValidator();
        if (motivo == "corto")
            v.TestValidate(new DarDeBajaAdjuntoCommand("proveedor", Guid.NewGuid(), Guid.NewGuid(), motivo))
                .ShouldNotHaveValidationErrorFor(x => x.Motivo);
        else
            v.TestValidate(new DarDeBajaAdjuntoCommand("proveedor", Guid.NewGuid(), Guid.NewGuid(), motivo))
                .ShouldHaveValidationErrorFor(x => x.Motivo);
        v.TestValidate(new DarDeBajaAdjuntoCommand("proveedor", Guid.NewGuid(), Guid.NewGuid(), new string('x', 501)))
            .ShouldHaveValidationErrorFor(x => x.Motivo);
    }

    // ------------------------------------------------------ enlace y contenido

    [Fact]
    public async Task Enlace_y_descarga_por_token_audita_y_devuelve_el_contenido()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var a = await SubirOkAsync(e, id, Siembra.TipoContrato);

        var enlace = await new EmitirEnlaceDescargaHandler(e.Acceso, e.Db, e.Enlaces)
            .Handle(new("proveedor", id, a.Id), default);
        enlace.Url.Should().StartWith(AdjuntoRutas.DescargaPorEnlace);
        e.Audit.Registros.Should().Contain(r => r.Operacion == "enlace" && r.EntidadId == a.Id);

        var token = Uri.UnescapeDataString(enlace.Url[AdjuntoRutas.DescargaPorEnlace.Length..]);
        var porEnlace = new ObtenerContenidoPorEnlaceHandler(e.Enlaces, e.Acceso, e.Db, e.Blob);
        var contenido = await porEnlace.Handle(new(token), default);

        contenido.Should().NotBeNull();
        using var ms = new MemoryStream();
        await contenido!.Contenido.CopyToAsync(ms);
        ms.ToArray().Should().Equal(Pdf);
        contenido.NombreArchivo.Should().Be("doc.pdf");
        e.Audit.Registros.Should().Contain(r => r.Operacion == "descarga" && r.EntidadId == a.Id);
    }

    [Fact]
    public async Task Token_invalido_devuelve_null_y_tras_la_baja_es_404()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var a = await SubirOkAsync(e, id, Siembra.TipoContrato);
        var token = e.Enlaces.Emitir(a.Id, e.Usuario.UserId!.Value).Token;
        var porEnlace = new ObtenerContenidoPorEnlaceHandler(e.Enlaces, e.Acceso, e.Db, e.Blob);

        (await porEnlace.Handle(new("basura"), default)).Should().BeNull();

        await new DarDeBajaAdjuntoHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id, a.Id, "Documento equivocado"), default);
        var act = () => porEnlace.Handle(new(token), default);
        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Code.Should().Be("ADJUNTO_NO_ENCONTRADO");

        var emitir = () => new EmitirEnlaceDescargaHandler(e.Acceso, e.Db, e.Enlaces).Handle(new("proveedor", id, a.Id), default);
        await emitir.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task Contenido_sin_permiso_de_ver_es_403_y_se_audita_la_denegacion()
    {
        var (e, id) = await PrepararAsync(permisos: SubirP);
        using var _ = e;

        var act = () => new ObtenerContenidoAdjuntoHandler(e.Acceso, e.Db, e.Blob).Handle(new("proveedor", id, Guid.NewGuid()), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        e.Audit.Registros.Should().Contain(r => r.Operacion == "denegacion");
    }

    // --------------------------------------------------- tipos/expediente/bitacora

    [Fact]
    public async Task Tipos_y_expediente_requieren_permiso_de_ver()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;

        (await new ListarTiposDocumentoHandler(e.Acceso, e.Db).Handle(new("proveedor"), default)).Should().HaveCount(5);
        var exp = await new ObtenerExpedienteHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id), default);
        exp.Completo.Should().BeFalse();
        exp.Documentos.Should().HaveCount(5);

        e.Permisos.Concedidos.Clear();
        await FluentActions.Awaiting(() => new ListarTiposDocumentoHandler(e.Acceso, e.Db).Handle(new("proveedor"), default))
            .Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Awaiting(() => new ObtenerExpedienteHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id), default))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Expediente_completo_con_cinco_documentos_subidos_por_handler()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        foreach (var t in new[] { Siembra.TipoCsf, Siembra.TipoContrato, Siembra.TipoActa, Siembra.TipoIdentificacion, Siembra.TipoDomicilio })
            await SubirOkAsync(e, id, t);

        var exp = await new ObtenerExpedienteHandler(e.Acceso, e.Db, e.Clock).Handle(new("proveedor", id), default);

        exp.Completo.Should().BeTrue();
        exp.Documentos.Should().OnlyContain(d => d.Actual != null && d.Estado != EstadoExpedienteDocumento.Faltante);
    }

    [Fact]
    public async Task Bitacora_requiere_permiso_de_baja_y_consulta_por_adjunto()
    {
        var (e, id) = await PrepararAsync();
        using var _ = e;
        var a = await SubirOkAsync(e, id, Siembra.TipoContrato);
        var mediator = new CapturaMediator();
        var handler = new ConsultarBitacoraAdjuntoHandler(e.Acceso, e.Db, mediator, e.Clock);

        await handler.Handle(new("proveedor", id, a.Id), default);

        mediator.Ultima.Should().NotBeNull();
        mediator.Ultima!.Recurso.Should().Be("Adjunto");
        mediator.Ultima.EntidadId.Should().Be(a.Id);
        mediator.Ultima.Desde.Should().BeOnOrBefore(mediator.Ultima.Hasta);

        e.Permisos.Concedidos.Remove(BajaP);
        await FluentActions.Awaiting(() => handler.Handle(new("proveedor", id, a.Id), default))
            .Should().ThrowAsync<ForbiddenException>();
    }

    private sealed class SinSeek : MemoryStream
    {
        public override bool CanSeek => false;
    }

    private sealed class CapturaMediator : IMediator
    {
        public ConsultarBitacoraQuery? Ultima { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Ultima = request as ConsultarBitacoraQuery;
            return Task.FromResult((TResponse)(object)new ConsultarBitacoraResponse([], 0));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => throw new NotSupportedException();
    }
}
