---
tags: [arquitectura, dotnet, configuracion, buenas-practicas]
title: Jerarquía y Sobrescritura de appsettings en .NET
---

# ⚙️ Jerarquía y Sobrescritura de Configuraciones (appsettings)

En ASP.NET Core, la configuración no se lee de un solo archivo de forma aislada, sino que se construye a través de una **cascada de sobrescritura**.

## ¿Por qué solo modificamos `appsettings.Development.json`?

Es una duda muy común. La respuesta es **SÍ**, `appsettings.Development.json` sobrescribe y complementa lo que hay en `appsettings.json` base. 

Cuando levantamos el proyecto localmente (el cual por defecto arranca bajo el entorno de `Development`), el framework lee los archivos en el siguiente orden estricto (lo último sobrescribe a lo anterior):

1. `appsettings.json` *(La base: contiene configuraciones por defecto para Producción/Generales).*
2. `appsettings.Development.json` *(Sobrescribe la base: contiene valores, URLs locales y credenciales exclusivas para el entorno local).*
3. **User Secrets (`secrets.json`)** *(Sobrescribe todo lo anterior: guarda secretos que NUNCA se suben a Git, como tu ClientSecret de EntraID).*
4. Variables de Entorno.
5. Argumentos de Línea de Comandos.

### Beneficios de este diseño en nuestro repositorio
- **Limpieza en el código:** Mantenemos el `appsettings.json` base completamente limpio y agnóstico.
- **Seguridad:** Nos aseguramos de que configuraciones de prueba (como usar `FakeForLocalDev`, tu OID personal como SuperAdmin, o la conexión a `localhost`) nunca se filtren a Producción accidentalmente. Al hacer despliegue, el servidor de Producción ignorará el archivo `Development` y usará sus propias variables.
- **Flexibilidad:** Si un campo existe en el base pero no en `Development`, se respeta el valor base. Si existe en ambos, `Development` gana.
