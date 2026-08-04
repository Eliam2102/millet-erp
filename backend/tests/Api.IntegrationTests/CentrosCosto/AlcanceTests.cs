using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Application.Asignaciones;
using Millet.CentrosCosto.Application.Asignaciones.Alcance;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// Tests del alcance congelado en máquinas (CECO-PR6, 01-diseno §7):
/// expansión backend por nivel con grupos acotados al padre, tri-estado
/// calculado hoja→arriba, filtro del selector con bypass, y el test de la
/// DECISIÓN DE NEGOCIO (sin re-evaluación en vivo). Sub-árboles aislados
/// creados vía handlers (claves únicas) + teardown en finally — inmunes a
/// ediciones del catálogo sembrado. El usuario es un Guid aleatorio: sus
/// filas de <c>asignaciones</c> no chocan con nadie.
/// </summary>
public class AlcanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AlcanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ─── Arnés del evaluador (molde AlcanceCajaTests: fakes + ctor real) ─────

    private sealed class FakeUserCtx(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public string? UserName => "test-alcance";
    }

    private sealed class FakePerms(params string[] permisos) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permisos.Contains(permiso, StringComparer.Ordinal));
    }

    private sealed record Fixture(
        Guid Grupo2A, Guid Grupo2B, Guid Grupo3,
        Guid Dim1X, Guid Dim1Y,
        Guid Dim2XA, Guid Dim2XB, Guid Dim2YA,
        Guid Dim3XA1, Guid Dim3XA2, Guid Dim3XAMuerta, Guid Dim3XB1, Guid Dim3YA1);

    /// <summary>
    /// Sub-árbol de prueba: dos Dim1 (X, Y) que COMPARTEN el grupo A —
    /// el acotamiento al padre se prueba contra Y. Bajo X-A: 2 Dim3 vivas
    /// + 1 inactiva (la expansión debe ignorarla).
    /// </summary>
    private static async Task<Fixture> CrearFixtureAsync(IMediator mediator, CentrosCostoDbContext db, string sufijo)
    {
        var grupo2A = await mediator.Send(new CrearGrupoDim2Command($"G2A-ALC {sufijo}"));
        var grupo2B = await mediator.Send(new CrearGrupoDim2Command($"G2B-ALC {sufijo}"));
        var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-ALC {sufijo}"));
        var dim1X = await mediator.Send(new CrearDim1Command($"8{sufijo[..3]}", $"PLANTA X ALC {sufijo}"));
        var dim1Y = await mediator.Send(new CrearDim1Command($"9{sufijo[..3]}", $"PLANTA Y ALC {sufijo}"));

        var dim2XA = await mediator.Send(new CrearDim2Command(dim1X.Id, $"XA{sufijo[..4]}", "Corte alc", grupo2A.Id));
        var dim2XB = await mediator.Send(new CrearDim2Command(dim1X.Id, $"XB{sufijo[..4]}", "Templado alc", grupo2B.Id));
        var dim2YA = await mediator.Send(new CrearDim2Command(dim1Y.Id, $"YA{sufijo[..4]}", "Corte alc Y", grupo2A.Id));

        var dim3XA1 = await mediator.Send(new CrearDim3Command(dim2XA.Id, $"XA1{sufijo[..3]}", "Gantry alc", grupo3.Id));
        var dim3XA2 = await mediator.Send(new CrearDim3Command(dim2XA.Id, $"XA2{sufijo[..3]}", "Mesa alc", grupo3.Id));
        var dim3XAMuerta = await mediator.Send(new CrearDim3Command(dim2XA.Id, $"XAM{sufijo[..3]}", "Muerta alc", grupo3.Id));
        var dim3XB1 = await mediator.Send(new CrearDim3Command(dim2XB.Id, $"XB1{sufijo[..3]}", "Horno alc", grupo3.Id));
        var dim3YA1 = await mediator.Send(new CrearDim3Command(dim2YA.Id, $"YA1{sufijo[..3]}", "Gantry Y alc", grupo3.Id));

        var versionMuerta = (await db.Dim3s.AsNoTracking().FirstAsync(e => e.Id == dim3XAMuerta.Id)).Version;
        await mediator.Send(new CambiarEstatusDim3Command(dim3XAMuerta.Id, versionMuerta, Activar: false));

        return new Fixture(
            grupo2A.Id, grupo2B.Id, grupo3.Id, dim1X.Id, dim1Y.Id,
            dim2XA.Id, dim2XB.Id, dim2YA.Id,
            dim3XA1.Id, dim3XA2.Id, dim3XAMuerta.Id, dim3XB1.Id, dim3YA1.Id);
    }

    private static async Task LimpiarAsync(CentrosCostoDbContext db, Guid usuarioId, Fixture? f)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM centros_costo.asignaciones WHERE usuario_id = {usuarioId}");
        if (f is null)
            return;

        foreach (var dim1Id in new[] { f.Dim1X, f.Dim1Y })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM centros_costo.dim3 WHERE dim2_id IN
                    (SELECT id FROM centros_costo.dim2 WHERE dim1_id = {dim1Id})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM centros_costo.dim2 WHERE dim1_id = {dim1Id}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM centros_costo.dim1 WHERE id = {dim1Id}");
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM centros_costo.grupos_dim2 WHERE id IN ({f.Grupo2A}, {f.Grupo2B})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM centros_costo.grupos_dim3 WHERE id = {f.Grupo3}");
    }

    private static Task<List<Guid>> AsignadasAsync(CentrosCostoDbContext db, Guid usuarioId) =>
        db.Asignaciones.AsNoTracking()
            .Where(a => a.UsuarioId == usuarioId)
            .Select(a => a.Dim3Id)
            .ToListAsync();

    [Fact]
    public async Task Marcar_grupo_acotado_al_padre_expande_exacto_ignora_muertas_y_es_idempotente()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            // ── 1. Marcar "grupo A bajo X" asigna EXACTAMENTE las 2 hojas vivas
            //       de X-A: ni la inactiva, ni las de X-B, ni las de Y-A (el
            //       mismo grupo A bajo OTRA dim1 — acotamiento al padre §7). ──
            var marca = await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.GrupoDim2BajoDim1, f.Dim1X, f.Grupo2A, Asignar: true));
            Assert.Equal(2, marca.HojasResueltas);
            Assert.Equal(2, marca.Afectadas);
            Assert.Equal(2, marca.TotalUsuario);

            var asignadas = await AsignadasAsync(db, usuario);
            Assert.Equal(
                new[] { f.Dim3XA1, f.Dim3XA2 }.OrderBy(g => g),
                asignadas.OrderBy(g => g));

            // ── 2. Idempotencia: la segunda marca no duplica ni truena. ──
            var remarca = await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.GrupoDim2BajoDim1, f.Dim1X, f.Grupo2A, Asignar: true));
            Assert.Equal(0, remarca.Afectadas);
            Assert.Equal(2, remarca.TotalUsuario);

            // ── 3. Desmarcar borra SOLO lo del nodo, no el resto del usuario. ──
            await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.Dim3, f.Dim3XB1, null, Asignar: true));
            var desmarca = await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.GrupoDim2BajoDim1, f.Dim1X, f.Grupo2A, Asignar: false));
            Assert.Equal(2, desmarca.Afectadas);
            Assert.Equal(1, desmarca.TotalUsuario);
            Assert.Equal(f.Dim3XB1, Assert.Single(await AsignadasAsync(db, usuario)));

            // ── 4. Marcar la Dim1 completa expande a TODAS sus vivas (X-A + X-B). ──
            var marcaDim1 = await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.Dim1, f.Dim1X, null, Asignar: true));
            Assert.Equal(3, marcaDim1.HojasResueltas); // XA1, XA2, XB1 — la muerta NO
            Assert.Equal(3, marcaDim1.TotalUsuario);

            // ── 5. Nodo inexistente → 404, sin tocar filas. ──
            await Assert.ThrowsAsync<EntityNotFoundException>(() => mediator.Send(
                new MarcarAlcanceCommand(usuario, NivelAlcance.Dim2, Guid.NewGuid(), null, Asignar: true)));
            Assert.Equal(3, (await AsignadasAsync(db, usuario)).Count);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    [Fact]
    public async Task Maquina_nueva_NO_entra_al_alcance_hasta_re_marcar()
    {
        // ═════════════════════════════════════════════════════════════════
        // POR QUÉ EXISTE ESTE TEST (léelo antes de "mejorarlo"):
        //
        // Es la decisión de negocio más discutida del módulo (01-diseno §7,
        // decidida 2026-07-16): el alcance se CONGELA al marcar — la regla
        // "todo el grupo" se usa para expandir y SE TIRA. NO hay
        // re-evaluación en vivo. Si Contabilidad crea una máquina nueva
        // bajo un grupo que alguien marcó "completo", esa persona NO la
        // obtiene hasta que un administrador vuelva a marcar.
        //
        // Si este test te estorba porque hiciste que el alcance se expanda
        // al vuelo (guardando la regla, evaluando el grupo en el evaluador,
        // o un trigger), no lo arregles: acabas de revertir la decisión.
        // El costo aceptado a cambio es visible aquí mismo: el renglón del
        // grupo pasa de "Todo" a "Parcial" — igual que si alguien hubiera
        // quitado la máquina a propósito. El tri-estado NO distingue
        // intención; eso también es §7, escrito a propósito.
        // ═════════════════════════════════════════════════════════════════
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            // Marcar "grupo 3 bajo X-A": quedan sus 2 vivas → el grupo pinta Todo.
            await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.GrupoDim3BajoDim2, f.Dim2XA, f.Grupo3, Asignar: true));

            var antes = await mediator.Send(new ObtenerArbolAsignacionQuery(usuario));
            var grupoAntes = BuscarGrupoDim3(antes, f.Dim2XA, f.Grupo3);
            Assert.Equal(TriEstado.Todo, grupoAntes.Estado);
            Assert.Equal(2, grupoAntes.Dim3Asignadas);

            // Nace una máquina nueva bajo ese MISMO grupo y dim2.
            var nueva = await mediator.Send(new CrearDim3Command(
                f.Dim2XA, $"NVA{sufijo[..3]}", "Recién creada", f.Grupo3));

            // El alcance congelado NO la incluye…
            var asignadas = await AsignadasAsync(db, usuario);
            Assert.Equal(2, asignadas.Count);
            Assert.DoesNotContain(nueva.Id, asignadas);

            // …y el árbol lo hace VISIBLE: el grupo cae a Parcial (2/3) y el
            // parcial sube por los 4 niveles (dim2, grupo-dim2, dim1).
            var despues = await mediator.Send(new ObtenerArbolAsignacionQuery(usuario));
            var grupoDespues = BuscarGrupoDim3(despues, f.Dim2XA, f.Grupo3);
            Assert.Equal(TriEstado.Parcial, grupoDespues.Estado);
            Assert.Equal(3, grupoDespues.Dim3Vivas);
            Assert.Equal(2, grupoDespues.Dim3Asignadas);

            var dim1X = despues.Dim1s.Single(d => d.Id == f.Dim1X);
            Assert.Equal(TriEstado.Parcial, dim1X.Estado);
            var grupoDim2A = dim1X.Grupos.Single(g => g.Id == f.Grupo2A);
            Assert.Equal(TriEstado.Parcial, grupoDim2A.Estado);
            Assert.Equal(TriEstado.Parcial, grupoDim2A.Dim2s.Single(d => d.Id == f.Dim2XA).Estado);

            // Re-marcar es EL mecanismo para incorporarla (no hay otro).
            var remarca = await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.GrupoDim3BajoDim2, f.Dim2XA, f.Grupo3, Asignar: true));
            Assert.Equal(1, remarca.Afectadas);

            var final = await mediator.Send(new ObtenerArbolAsignacionQuery(usuario));
            Assert.Equal(TriEstado.Todo, BuscarGrupoDim3(final, f.Dim2XA, f.Grupo3).Estado);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    [Fact]
    public async Task Arbol_5_niveles_quitar_una_hoja_pinta_parcial_hacia_arriba_y_resumen_coherente()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            // Marcar la Dim1 X completa → Todo en toda su rama.
            await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.Dim1, f.Dim1X, null, Asignar: true));

            var completo = await mediator.Send(new ObtenerArbolAsignacionQuery(usuario));
            var dim1X = completo.Dim1s.Single(d => d.Id == f.Dim1X);
            Assert.Equal(TriEstado.Todo, dim1X.Estado);
            Assert.Equal(3, dim1X.Dim3Vivas); // la muerta no cuenta ni se pinta
            Assert.Equal(2, dim1X.Grupos.Count); // A y B, acotados a X
            Assert.Equal(1, completo.Resumen.Dim1Completas); // solo X (el resto del catálogo: Ninguno)

            // DoD: QUITAR UNA HOJA pinta "parcial" HACIA ARRIBA en los 4 niveles.
            await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.Dim3, f.Dim3XA1, null, Asignar: false));

            var parcial = await mediator.Send(new ObtenerArbolAsignacionQuery(usuario));
            dim1X = parcial.Dim1s.Single(d => d.Id == f.Dim1X);
            var grupoA = dim1X.Grupos.Single(g => g.Id == f.Grupo2A);
            var dim2XA = grupoA.Dim2s.Single(d => d.Id == f.Dim2XA);
            var grupo3XA = dim2XA.Grupos.Single(g => g.Id == f.Grupo3);

            Assert.Equal(TriEstado.Parcial, grupo3XA.Estado); // nivel 4
            Assert.Equal(TriEstado.Parcial, dim2XA.Estado);   // nivel 3
            Assert.Equal(TriEstado.Parcial, grupoA.Estado);   // nivel 2
            Assert.Equal(TriEstado.Parcial, dim1X.Estado);    // nivel 1
            Assert.False(grupo3XA.Dim3s.Single(e => e.Id == f.Dim3XA1).Asignada);

            // La rama B de X sigue Todo — el parcial no contamina hermanos.
            var grupoB = dim1X.Grupos.Single(g => g.Id == f.Grupo2B);
            Assert.Equal(TriEstado.Todo, grupoB.Estado);

            // Resumen coherente con los renglones (barra por dimensión).
            Assert.Equal(0, parcial.Resumen.Dim1Completas);
            Assert.Equal(
                parcial.Dim1s.Sum(d => d.Dim3Asignadas),
                parcial.Resumen.Dim3Asignadas);
            Assert.Equal(2, parcial.Resumen.Dim3Asignadas); // XA2 + XB1
            Assert.Equal(
                parcial.Dim1s.Sum(d => d.Dim3Vivas),
                parcial.Resumen.Dim3Vivas);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    [Fact]
    public async Task Selector_filtra_por_alcance_usuario_sin_filas_vacio_y_bypass_leer_todos()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var usuario = Guid.NewGuid();
        Fixture? f = null;

        // Arnés molde AlcanceCajaTests: el evaluador/handler se construyen a
        // mano con fakes de usuario y permisos + el DbContext real.
        BuscarDim3Handler Selector(Guid? userId, params string[] permisos) =>
            new(db, new AlcanceDim3Evaluator(db, new FakeUserCtx(userId), new FakePerms(permisos)));

        try
        {
            f = await CrearFixtureAsync(mediator, db, sufijo);

            // ── 1. DoD: usuario sin filas = selector VACÍO (aunque el catálogo esté lleno). ──
            var vacio = await Selector(usuario).Handle(
                new BuscarDim3Query(null, IncluirInactivas: false, Limit: 100), CancellationToken.None);
            Assert.Empty(vacio);

            // ── 2. Con alcance: el selector ofrece SOLO lo asignado. ──
            await mediator.Send(new MarcarAlcanceCommand(
                usuario, NivelAlcance.Dim3, f.Dim3XA1, null, Asignar: true));

            var filtrado = await Selector(usuario).Handle(
                new BuscarDim3Query(null, IncluirInactivas: false, Limit: 100), CancellationToken.None);
            var item = Assert.Single(filtrado);
            Assert.Equal(f.Dim3XA1, item.Id);
            Assert.Equal($"XA1{sufijo[..3]}", item.Clave); // display con contexto, nunca el Guid

            // ── 3. Bypass dim3.leer-todos: todo el catálogo SIN filas propias.
            //       Se usa la constante CANÓNICA de Identidad a propósito: si
            //       el espejo interno del evaluador driftea, esto truena. ──
            var total = await Selector(Guid.NewGuid(), Millet.Identidad.Domain.PermisosCanonicos.CentrosCostoDim3LeerTodos).Handle(
                new BuscarDim3Query(null, IncluirInactivas: false, Limit: 100), CancellationToken.None);
            Assert.Equal(100, total.Count); // clamp del typeahead sobre 361+ sembradas

            // ── 4. Sin usuario en el contexto (token raro) degrada a Ninguno, no a todo. ──
            var sinUsuario = await Selector(null).Handle(
                new BuscarDim3Query(null, IncluirInactivas: false, Limit: 100), CancellationToken.None);
            Assert.Empty(sinUsuario);
        }
        finally
        {
            await LimpiarAsync(db, usuario, f);
        }
    }

    private static GrupoDim3AsignacionDto BuscarGrupoDim3(
        ArbolAsignacionResponse arbol, Guid dim2Id, Guid grupo3Id) =>
        arbol.Dim1s
            .SelectMany(d1 => d1.Grupos)
            .SelectMany(g2 => g2.Dim2s)
            .Single(d2 => d2.Id == dim2Id)
            .Grupos.Single(g3 => g3.Id == grupo3Id);
}
