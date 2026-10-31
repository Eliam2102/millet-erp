using System.Globalization;
using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.DatosMaestros.Application.Cargas;

public sealed record CambioMaestros(string Campo, string? Antes, string? Despues);
public sealed record RenglonMaestros(int Renglon, string Estado, IReadOnlyDictionary<string, string> Valores,
    IReadOnlyList<CambioMaestros> Cambios, IReadOnlyList<ErrorMaestros> Errores);
public sealed record ResultadoMaestros(string Huella, IReadOnlyList<RenglonMaestros> Renglones, int Nuevos, int Actualiza, int SinCambio, int Errores, bool Confirmado, IReadOnlyList<ConteoMaestros>? Resumen = null);
public sealed record ConteoMaestros(string Seccion, string Clave, int Total);
public sealed record PlanMaestros(object? Comando, IReadOnlyList<string> Llaves, IReadOnlyList<CambioMaestros> Cambios, bool Nuevo);
public interface IAdaptadorCargaMaestros
{
    string Catalogo { get; }
    DbContext Db { get; }
    IReadOnlyList<ColumnaCarga> Columnas { get; }
    Task PrepararArchivoAsync(TablaMaestros tabla, CancellationToken cancellationToken) => Task.CompletedTask;
    IReadOnlyList<string> LlavesArchivo(FilaMaestros fila);
    Task<PlanMaestros> PrepararAsync(FilaMaestros fila, CancellationToken ct);
    IReadOnlyList<ConteoMaestros>? Resumir(IReadOnlyList<RenglonMaestros> renglones) => null;
}
public static class CargaMaestrosDatos
{
    public static string? Texto(this FilaMaestros fila, string campo) => fila.Valores.TryGetValue(campo, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    public static string Requerido(this FilaMaestros fila, string campo) => fila.Texto(campo) ?? throw Error(campo, "Capture este dato obligatorio.");
    public static BusinessRuleException Error(string campo, string mensaje) => new("CARGA_CAMPO:" + campo, mensaje);
    public static Guid? Id(this FilaMaestros fila, string campo) => fila.Texto(campo) is not { } v ? null : Guid.TryParse(v, out var id) && id != Guid.Empty ? id : throw Error(campo, "Use un identificador válido del catálogo.");
    public static PlanMaestros Plan(object? actual, object comando, IEnumerable<string> llaves, params (string Campo, object? Antes, object? Despues)[] campos)
    {
        static string? Texto(object? valor) => valor is null ? null : Convert.ToString(valor, CultureInfo.InvariantCulture);
        var cambios = campos.Where(c => c.Despues is not null && !Equals(c.Antes, c.Despues))
            .Select(c => new CambioMaestros(c.Campo, Texto(c.Antes), Texto(c.Despues))).ToList();
        return new(actual is null || cambios.Count > 0 ? comando : null, llaves.ToList(), cambios, actual is null);
    }
}

public sealed class CargaMaestrosServicio(IEnumerable<IAdaptadorCargaMaestros> adaptadores, ISender sender, IServiceProvider servicios)
{
    public IAdaptadorCargaMaestros Adaptador(string catalogo) => adaptadores.SingleOrDefault(a => a.Catalogo == catalogo)
        ?? throw new EntityNotFoundException("CARGA_CATALOGO_NO_EXISTE", "El catálogo de carga no existe.");

    public async Task<ResultadoMaestros> EjecutarAsync(string catalogo, byte[] archivo, string nombre, bool confirmar, string? huellaEsperada, CancellationToken ct)
    {
        var adaptador = Adaptador(catalogo);
        var huella = Convert.ToHexString(SHA256.HashData(archivo));
        if (confirmar && !string.Equals(huella, huellaEsperada, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("CARGA_HUELLA_DISTINTA", "El archivo cambió. Genere de nuevo la vista previa antes de confirmar.");
        var tabla = ArchivoMaestros.Leer(archivo, nombre, adaptador.Columnas);
        var numeros = tabla.Filas.Select(f => f.Numero).ToHashSet();
        var erroresEncabezado = tabla.Errores.Where(e => !numeros.Contains(e.Renglon)).ToList();
        var llavesArchivo = tabla.Filas.ToDictionary(f => f.Numero, f => adaptador.LlavesArchivo(f));
        var resultados = new List<RenglonMaestros>(); var planes = new Dictionary<int, PlanMaestros>();
        // La previa sólo consulta. Confirmar serializa el catálogo antes de volver a resolver las llaves.
        await using var tx = confirmar ? await adaptador.Db.Database.BeginTransactionAsync(ct) : null;
        if (confirmar && adaptador.Db.Database.IsNpgsql())
            await adaptador.Db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({catalogo}, 41011))", ct);
        await adaptador.PrepararArchivoAsync(tabla, ct);
        foreach (var fila in tabla.Filas)
        {
            var errores = tabla.Errores.Where(e => e.Renglon == fila.Numero).Concat(erroresEncabezado).ToList();
            PlanMaestros? plan = null;
            if (errores.Count == 0)
            {
                try
                {
                    plan = await adaptador.PrepararAsync(fila, ct);
                    if (plan.Comando is { } comando)
                    {
                        var tipo = typeof(IValidator<>).MakeGenericType(comando.GetType());
                        foreach (var validador in servicios.GetServices(tipo).OfType<IValidator>())
                        {
                            var validacion = await validador.ValidateAsync(new ValidationContext<object>(comando), ct);
                            errores.AddRange(validacion.Errors.Select(e => new ErrorMaestros(fila.Numero, Columna(adaptador, e.PropertyName), e.ErrorCode, e.ErrorMessage)));
                        }
                    }
                    planes[fila.Numero] = plan;
                }
                catch (DomainException e) { errores.Add(Error(fila.Numero, e)); }
                catch (Exception e) when (e is FormatException or OverflowException) { errores.Add(new(fila.Numero, "Dato", "FORMATO_INVALIDO", "Revise el formato numérico del renglón.")); }
            }
            resultados.Add(new(fila.Numero, errores.Count > 0 ? "Error" : plan!.Nuevo ? "Nuevo" : plan.Comando is null ? "SinCambio" : "Actualiza", fila.Valores, plan?.Cambios ?? [], errores));
        }
        var duplicados = llavesArchivo.SelectMany(p => p.Value.Concat(planes.GetValueOrDefault(p.Key)?.Llaves ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Select(k => (Fila: p.Key, Llave: k)))
            .GroupBy(x => x.Llave, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g.Select(x => x.Fila)).ToHashSet();
        for (var i = 0; i < resultados.Count; i++)
        {
            var fila = resultados[i];
            if (duplicados.Contains(fila.Renglon))
                resultados[i] = fila with { Estado = "Error", Errores = fila.Errores.Append(new(fila.Renglon, "Clave", "DUPLICADO_EN_ARCHIVO", "El registro está repetido en el archivo. Corrija todos sus renglones antes de cargar.")).ToList() };
        }
        if (confirmar)
        {
            for (var i = 0; i < resultados.Count; i++)
            {
                var fila = resultados[i];
                if (fila.Estado is "Error" or "SinCambio") continue;
                await tx!.CreateSavepointAsync("renglon", ct);
                try
                {
                    // Una regla fiscal anterior del mismo lote puede haber actualizado la versión de un producto.
                    var vigente = await adaptador.PrepararAsync(new(fila.Renglon, fila.Valores.ToDictionary(x => x.Key, x => x.Value)), ct);
                    if (vigente.Comando is not null) await sender.Send(vigente.Comando, ct);
                    else resultados[i] = fila with { Estado = "SinCambio", Cambios = [] };
                }
                catch (Exception e) when (e is DomainException or DbUpdateException)
                {
                    await tx.RollbackToSavepointAsync("renglon", ct);
                    adaptador.Db.ChangeTracker.Clear();
                    resultados[i] = fila with { Estado = "Error", Errores = [e is DomainException dominio ? Error(fila.Renglon, dominio) : new(fila.Renglon, "Registro", "CONFLICTO_DATOS", "El registro entró en conflicto. Revise su clave y referencias y genere otra vista previa.")] };
                }
            }
            await tx!.CommitAsync(ct);
        }
        return new(huella, resultados, resultados.Count(r => r.Estado == "Nuevo"), resultados.Count(r => r.Estado == "Actualiza"), resultados.Count(r => r.Estado == "SinCambio"), resultados.Count(r => r.Estado == "Error"), confirmar, adaptador.Resumir(resultados));
    }
    private static string Columna(IAdaptadorCargaMaestros adaptador, string propiedad) => adaptador.Columnas.FirstOrDefault(c =>
        c.Alias.Append(c.Nombre).Any(a => ArchivoMaestros.Normalizar(a) == ArchivoMaestros.Normalizar(propiedad)))?.Nombre ?? "Registro";
    private static ErrorMaestros Error(int numero, DomainException e) => e is Millet.SharedKernel.Application.Exceptions.ValidationException v
        ? new(numero, v.Errors[0].Field, v.Errors[0].Code, v.Errors[0].Message)
        : new(numero, e.Code.StartsWith("CARGA_CAMPO:", StringComparison.Ordinal) ? e.Code[12..] : "Registro", e.Code, e.Message);
}
