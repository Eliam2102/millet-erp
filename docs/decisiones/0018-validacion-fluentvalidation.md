# ADR-0018: Validación de input con FluentValidation y reglas de negocio en handlers

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: validación, api, dominio, tier-2

## Contexto y problema

Cada endpoint del API recibe un request que debe validarse antes de
procesarlo. Las validaciones tienen dos naturalezas distintas:

- **Estructurales**: forma del request — campos requeridos, formatos (RFC con regex, longitudes, rangos numéricos, fechas razonables). Son rápidas, deterministas, sin acceso a BD
- **De negocio**: reglas que dependen del estado del sistema — "este RFC ya está registrado en la empresa", "el cliente está suspendido", "el periodo contable está cerrado", "el saldo es insuficiente". Requieren consultar BD y conocer contexto

Sin convenciones claras, los devs mezclan ambas en cualquier lugar
(controllers, handlers, atributos), los mensajes son inconsistentes (unos
en inglés, otros en español, unos genéricos, otros específicos), y las
reglas reusables (validar RFC, CURP, CLABE) se reescriben cada vez.

Necesitamos definir dónde vive cada tipo de validación, qué librerías se
usan, y cómo se mapean los errores a HTTP de forma uniforme.

## Drivers de la decisión

- Separación clara entre validación estructural (síncrona, barata) y de negocio (async, contextual)
- Mensajes en español consistentes
- Reglas reusables (RFC, CURP, montos positivos, etc.) declaradas una sola vez
- Integración con `ProblemDetails` (ADR-0010) para errores uniformes
- Compatibilidad con la generación de OpenAPI (ADR-0017): los validators no deben distorsionar la spec
- Performance: validación estructural no debe acceder a BD

## Opciones consideradas

1. FluentValidation para input + excepciones de dominio en handlers
2. DataAnnotations sobre DTOs + validación manual en handlers
3. Solo validación manual (sin librería) en cada handler
4. FluentValidation para todo, incluyendo reglas de negocio con `MustAsync`

## Decisión

Se adopta la **opción 1**: dos capas de validación con responsabilidades
claras.

### Capa 1: Validación de input con FluentValidation

**Alcance**: forma del request. Campos requeridos, formatos, longitudes,
rangos. NO accede a BD.

**Estructura por módulo**:

```
backend/src/Comercial/
└── Application/
    └── Commands/
        └── CrearCliente/
            ├── CrearClienteCommand.cs
            ├── CrearClienteCommandValidator.cs    ← FluentValidation
            └── CrearClienteCommandHandler.cs
```

**Validator típico**:

```csharp
public class CrearClienteCommandValidator : AbstractValidator<CrearClienteCommand>
{
    public CrearClienteCommandValidator()
    {
        RuleFor(x => x.Rfc).EsRfcValido();
        RuleFor(x => x.RazonSocial)
            .NotEmpty().WithMessage("La razón social es obligatoria")
            .MaximumLength(254).WithMessage("La razón social no puede exceder 254 caracteres");
        RuleFor(x => x.RegimenFiscal)
            .NotEmpty().WithMessage("El régimen fiscal es obligatorio");
        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("El email no tiene formato válido")
            .When(x => !string.IsNullOrEmpty(x.Email));
    }
}
```

**Mecánica**:

- Validators heredan de `AbstractValidator<T>`
- Registrados automáticamente en DI vía `services.AddValidatorsFromAssemblyContaining<...>()` (escaneo de ensamblado, sin registro manual)
- Middleware ejecuta el validator antes del handler. Si falla, **interrumpe la ejecución** y retorna `400 Bad Request` con `ProblemDetails`
- Cada error se mapea al array `errores[]` de `ProblemDetails`:
  ```json
  {
    "type": "https://millet-erp/errors/validation",
    "title": "Errores de validación",
    "status": 400,
    "errores": [
      { "campo": "rfc", "codigo": "RFC_INVALIDO", "mensaje": "Formato de RFC inválido" },
      { "campo": "razonSocial", "codigo": "REQUIRED", "mensaje": "La razón social es obligatoria" }
    ]
  }
  ```
- El frontend usa el array `errores[]` para mostrar mensajes inline en cada campo del formulario

**Restricción importante**: los validators de input son **síncronos y sin
BD**. Si necesitas consultar BD para validar, eso es regla de negocio, no
input. NO usar `MustAsync` para validar contra BD (ver "Descartadas").

### Capa 2: Validación de reglas de negocio en handlers/domain services

**Alcance**: cualquier regla que requiere conocer el estado del sistema.

**Mecánica**:

- Vive dentro del handler o en domain services invocados por él
- Lanza excepciones específicas de dominio:
  - `BusinessRuleException`: violación de regla de negocio → HTTP 422
  - `EntityNotFoundException`: recurso referenciado no existe → HTTP 404
  - `ConcurrencyException`: conflicto de versión → HTTP 409 (ADR-0012)
  - `ForbiddenException`: el usuario no puede realizar la acción aunque esté autenticado → HTTP 403
- Mapeo automático a `ProblemDetails` por el middleware (ADR-0010)
- Mensajes en español, específicos del contexto:
  ```csharp
  if (await _clientes.ExisteConRfcAsync(rfc, empresaId))
      throw new BusinessRuleException(
          "RFC_DUPLICADO",
          $"Ya existe un cliente con el RFC '{rfc}' en esta empresa");
  ```

**Ejemplo de handler completo**:

```csharp
public class CrearClienteCommandHandler
{
    public async Task<ClienteDto> Handle(CrearClienteCommand cmd, CancellationToken ct)
    {
        // (Capa 1 ya validó la forma; aquí solo reglas de negocio)

        if (await _clientes.ExisteConRfcAsync(cmd.Rfc, _empresaContext.Current))
            throw new BusinessRuleException(
                "RFC_DUPLICADO",
                $"Ya existe un cliente con el RFC '{cmd.Rfc}' en esta empresa");

        if (!await _catalogoSat.ExisteRegimenFiscalAsync(cmd.RegimenFiscal))
            throw new EntityNotFoundException(
                "REGIMEN_FISCAL_INVALIDO",
                $"El régimen fiscal '{cmd.RegimenFiscal}' no existe en el catálogo SAT");

        var cliente = new Cliente(cmd.Rfc, cmd.RazonSocial, cmd.RegimenFiscal);
        await _clientes.AddAsync(cliente);
        await _uow.SaveChangesAsync(ct);

        return _mapper.ToDto(cliente);
    }
}
```

### Validaciones reusables (extensions)

Reglas comunes se definen una sola vez como extensions de `IRuleBuilder`:

```csharp
public static class CommonValidators
{
    public static IRuleBuilderOptions<T, string> EsRfcValido<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("El RFC es obligatorio")
            .Length(12, 13).WithMessage("El RFC debe tener 12 o 13 caracteres")
            .Matches(@"^[A-ZÑ&]{3,4}\d{6}[A-Z\d]{3}$")
                .WithMessage("Formato de RFC inválido");

    public static IRuleBuilderOptions<T, string> EsCurpValida<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("La CURP es obligatoria")
            .Length(18).WithMessage("La CURP debe tener 18 caracteres")
            .Matches(@"^[A-Z]{4}\d{6}[HM][A-Z]{5}[A-Z\d]\d$")
                .WithMessage("Formato de CURP inválido");

    public static IRuleBuilderOptions<T, string> EsClabeValida<T>(this IRuleBuilder<T, string> rule)
        => rule
            .Length(18).WithMessage("La CLABE debe tener 18 dígitos")
            .Matches(@"^\d{18}$").WithMessage("La CLABE solo puede contener dígitos");

    public static IRuleBuilderOptions<T, decimal> EsMontoPositivo<T>(this IRuleBuilder<T, decimal> rule)
        => rule.GreaterThan(0).WithMessage("El monto debe ser mayor a cero");

    public static IRuleBuilderOptions<T, string> EsMonedaSat<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("La moneda es obligatoria")
            .Length(3).WithMessage("El código de moneda debe tener 3 caracteres ISO 4217");
}
```

**Uso**:

```csharp
RuleFor(x => x.Rfc).EsRfcValido();
RuleFor(x => x.Total).EsMontoPositivo();
```

Vivienda: `backend/src/Shared/Application/Validation/CommonValidators.cs`.
Compartido entre módulos.

### Mensajes en español

- Todos los mensajes de validación son en español, hardcoded en los validators
- Convención: mensajes claros, accionables, sin jerga técnica
  - ✅ "El RFC es obligatorio"
  - ✅ "El RFC debe tener 12 o 13 caracteres"
  - ❌ "RFC must not be empty"
  - ❌ "Validation failed: NotEmptyValidator"
- Si en el futuro hay que soportar otros idiomas, FluentValidation tiene mecanismo de localización; será un ADR aparte

### Códigos de error

Cada error de negocio tiene un **código identificador** además del mensaje:

```csharp
throw new BusinessRuleException(
    code: "RFC_DUPLICADO",
    message: $"Ya existe un cliente con el RFC '{rfc}' en esta empresa");
```

- El frontend puede usar el `code` para lógica programática (mostrar diálogo distinto, sugerir acción) sin parsear el `message`
- Convención: códigos en `SCREAMING_SNAKE_CASE`, descriptivos, estables (cambiarlos rompe el frontend)
- Catálogo de códigos vive en `Shared/Application/ErrorCodes.cs` para evitar typos:
  ```csharp
  public static class ErrorCodes
  {
      public const string RfcDuplicado = "RFC_DUPLICADO";
      public const string PeriodoCerrado = "PERIODO_CERRADO";
      // ...
  }
  ```

### Integración con OpenAPI

- FluentValidation **no afecta la spec** automáticamente (no hay attributes que OpenAPI lea)
- Las reglas de validación se documentan en el `<remarks>` del endpoint si son críticas para el consumidor:
  ```csharp
  /// <summary>Crea un cliente nuevo.</summary>
  /// <remarks>
  /// El RFC debe tener 12 o 13 caracteres con formato SAT.
  /// La razón social máximo 254 caracteres.
  /// </remarks>
  ```
- Si en el futuro se quiere reflejar las reglas en la spec automáticamente, evaluar `FluentValidation.AspNetCore` con generador de OpenAPI; por ahora no es prioritario

### Validación en frontend

- Las reglas estructurales se **duplican** en el frontend (con react-hook-form + Zod, decisión Tier-2 separada) para feedback inmediato sin round-trip
- El duplicado es aceptable: la fuente de verdad es el backend, el frontend hace mejor UX
- Las reglas de negocio NO se duplican: solo el backend las conoce. Si fallan, el frontend muestra el mensaje del 422 mediante el componente de manejo de errores genérico

### Lo que esta ADR explícitamente NO cubre

- **react-hook-form + Zod en frontend**: ADR aparte (Tier-2 pendiente)
- **Localización de mensajes**: cuando se necesite
- **Validation pipeline con MediatR behaviors**: depende de la decisión sobre MediatR (Mediator de Othamar es la opción favorita; cuando se adopte, se integrará el pipeline de FluentValidation como `IPipelineBehavior`)
- **Validators async con `MustAsync`**: descartado deliberadamente (ver "Descartadas")

## Consecuencias

**Positivas**
- Separación clara: input estructural vs reglas de negocio
- Validators dedicados (no atributos sobre DTOs) permiten reglas complejas sin contaminar tipos
- Reglas reusables como extensions reducen duplicación
- Mensajes en español consistentes desde el inicio
- Códigos de error estables permiten lógica del frontend sin parsear mensajes
- Integración limpia con `ProblemDetails` (ADR-0010)
- Validators son testeables aisladamente (sin levantar la app)
- Validación de input es rápida (cero BD): protege handlers contra requests malformados sin overhead

**Negativas**
- Más archivos: cada command tiene su validator separado. Boilerplate inicial; paga a partir del primer command con 5+ reglas
- Disciplina obligatoria: si un dev mete reglas de negocio en el validator (con `MustAsync`), rompe la separación. Mitigado por code review y por documentación clara en `CLAUDE.md`
- Mensajes hardcoded en español: si surge necesidad de localización, hay que retrofittear (aceptable, las llaves de mensaje existirán)

## Descartadas

**DataAnnotations** (`[Required]`, `[StringLength]`, etc.) sobre DTOs.
Funciona para casos triviales pero se rompe en cuanto se necesita una
regla compleja: validación condicional, mensaje específico por contexto,
reglas que dependen de otros campos, validación de colecciones. Y mezcla
shape de datos con reglas, lo cual ensucia los DTOs.

**Solo validación manual sin librería**. Funciona pero termina siendo
inconsistente: cada dev escribe sus propios `if (string.IsNullOrEmpty(...))`
con su propio mensaje. Sin reusabilidad real, los códigos y mensajes
divergen.

**FluentValidation con `MustAsync` para reglas de negocio**.
Tentador (todo en un solo lugar) pero rompe el patrón:
- El validator necesita `DbContext`, `IClock`, `IEmpresaContext`, etc. inyectados, lo cual lo acopla a infraestructura
- Mezclar reglas síncronas estructurales con consultas async a BD esconde el costo (un validator que parecía barato termina haciendo 5 queries)
- Errores de negocio terminan como 400 (Bad Request) cuando deberían ser 422 (Unprocessable Entity), debilitando la semántica HTTP
- Los handlers terminan más limpios, pero la lógica de negocio queda dispersa entre validators y handlers

Mejor mantener validators **estructurales y rápidos**, y reglas de negocio
en handlers donde tienen acceso natural al UoW y al contexto.

## Notas de implementación

**Backend**

- Agregar paquete `FluentValidation.AspNetCore` (o `FluentValidation.DependencyInjectionExtensions`) a `Directory.Packages.props`
- Registro automático en `Program.cs`:
  ```csharp
  services.AddValidatorsFromAssemblyContaining<CrearClienteCommandValidator>();
  // o múltiples ensamblados:
  services.AddValidatorsFromAssemblies(new[] {
      typeof(IdentidadAssemblyMarker).Assembly,
      typeof(ComercialAssemblyMarker).Assembly,
      // ...
  });
  ```
- Middleware o filter `ValidationMiddleware` que ejecuta el validator correspondiente al request type antes del handler:
  - Resuelve `IValidator<TRequest>` desde DI
  - Si no hay validator registrado para el tipo, pasa al siguiente middleware (no es error)
  - Si hay validator y falla, retorna 400 con `ProblemDetails` formado desde `ValidationResult.Errors`
- Cada error se mapea a `{ campo, codigo, mensaje }`:
  - `campo` = `error.PropertyName` (lower-camel-case en JSON: `razonSocial`)
  - `codigo` = `error.ErrorCode` si está definido, sino derivado del nombre del validator (`NotEmptyValidator` → `REQUIRED`)
  - `mensaje` = `error.ErrorMessage`

**Códigos derivados**:

```csharp
public static class ValidatorCodeMapper
{
    public static string MapToCode(string fluentValidatorName) => fluentValidatorName switch
    {
        "NotEmptyValidator" => "REQUIRED",
        "NotNullValidator" => "REQUIRED",
        "EmailValidator" => "EMAIL_INVALIDO",
        "MaximumLengthValidator" => "LONGITUD_EXCEDIDA",
        "MinimumLengthValidator" => "LONGITUD_INSUFICIENTE",
        "GreaterThanValidator" => "FUERA_DE_RANGO",
        "RegularExpressionValidator" => "FORMATO_INVALIDO",
        _ => "VALIDATION_ERROR"
    };
}
```

Validators custom (`EsRfcValido`, etc.) declaran su propio `WithErrorCode`:

```csharp
.Matches(@"^[A-ZÑ&]{3,4}\d{6}[A-Z\d]{3}$")
    .WithMessage("Formato de RFC inválido")
    .WithErrorCode("RFC_INVALIDO");
```

**Excepciones de dominio**

- `BusinessRuleException(string code, string message)` → mapeada a 422
- `EntityNotFoundException(string code, string message)` → mapeada a 404
- `ForbiddenException(string code, string message)` → mapeada a 403
- Vivienda: `Shared/Application/Exceptions/`
- Constantes de códigos: `Shared/Application/ErrorCodes.cs`

**Tests**

- Cada validator tiene su test unit dedicado:
  ```csharp
  public class CrearClienteCommandValidatorTests
  {
      private readonly CrearClienteCommandValidator _validator = new();

      [Fact]
      public void Should_FailWhen_RfcEsVacio() { ... }

      [Fact]
      public void Should_PassWhen_DatosCorrectos() { ... }
  }
  ```
- FluentValidation provee helpers para tests: `TestValidate(...)`, `ShouldHaveValidationErrorFor(...)`

**Documentación en `CLAUDE.md`**

- Cuándo escribir un validator (input estructural) vs cuándo lanzar excepción (regla de negocio)
- Cómo agregar un código de error nuevo
- Cómo crear un validator reusable como extension
- Convenciones de mensajes en español
- Cómo testear un validator

**ADRs hijo posibles**

- Localización de mensajes (cuando se requiera)
- Pipeline de validación con behaviors cuando se adopte mediator (Othamar) en Fase 2+
- Validación de archivos subidos (CFDIs XML, adjuntos)
- Estrategia de validación frontend con react-hook-form + Zod (ADR Tier-2 pendiente)
