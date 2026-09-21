# ADR-0049: Entra ID sólo autentica; autorización y empresas viven en el ERP

- **Estado:** Aceptada para la ejecución de Fase 1
- **Fecha:** 2026-09-20
- **Alcance:** Autenticación, usuarios, empresas, roles y permisos
- **Validación externa de Millet:** Por confirmar

## Decisión

Microsoft Entra ID se usa exclusivamente como proveedor corporativo de
identidad. El ERP recibe y valida el token de Entra, usa el `oid` como vínculo
estable con `identidad.usuarios` y toma del token sólo los datos básicos de
identificación requeridos para iniciar o aprovisionar la sesión.

En Fase 1 no se usarán grupos, roles de aplicación ni configuración de
autorización de Microsoft para gobernar el ERP.

El ERP es dueño de:

- usuarios internos y su vínculo con el `oid` de Entra;
- empresas accesibles por usuario;
- roles por empresa;
- permisos funcionales por rol;
- empresa activa de la sesión;
- segregación y auditoría de acceso.

## Convergencia entre ADM-01 y ADM-02

```text
Microsoft Entra ID
  └─ autentica y entrega oid/email/nombre
       └─ identidad.usuarios (ERP)
            └─ usuario_empresa_roles
                 ├─ empresa creada/administrada en ADM-01
                 └─ rol y permisos administrados en ADM-02
                      └─ JWT del ERP con current_empresa_id
```

Geovany, responsable del frente organizacional, entrega empresas y su
estructura. Uzziel, responsable del frente de autenticación e identidad,
entrega el vínculo `EntraOid → Usuario ERP`. Ambos convergen en la asignación
`UsuarioEmpresaRol`, que sólo puede cerrarse con una prueba conjunta.

## Comportamiento requerido

1. Entra valida que la persona es quien dice ser.
2. El ERP localiza o aprovisiona el usuario mediante `EntraOid`.
3. Un usuario nuevo queda sin empresas ni permisos hasta una asignación
   explícita dentro del ERP.
4. El ERP obtiene las empresas accesibles desde `UsuarioEmpresaRol`.
5. Con una empresa se selecciona automáticamente; con varias se muestra el
   selector; con ninguna no se habilita operación funcional.
6. Cambiar de empresa exige una asignación existente y reemite el JWT del ERP.
7. La API y el frontend consumen los permisos calculados por el ERP para la
   empresa activa.

## Código existente y deuda controlada

`LoginOrchestrator` ya implementa la convergencia principal mediante
`EntraOid`, `UsuarioEmpresaRol`, selección/cambio de empresa y carga de
permisos. La asociación opcional `RolGrupoEntraId` también existe en el
repositorio, pero queda fuera del flujo y del alcance de Fase 1 por esta
decisión.

No se elimina esa capacidad en el mismo PR de cierre de ADM-02. Su eliminación
o desactivación visible se tratará como deuda técnica separada para no mezclar
autenticación, cambio de alcance y limpieza transversal.

## Pruebas de aceptación compartidas

- `oid` válido + usuario sin asignación: autentica, pero no opera una empresa.
- Usuario asignado a empresa A: recibe A y sus permisos.
- Usuario asignado a A y B con roles distintos: puede cambiar y obtiene el set
  correspondiente.
- Usuario no asignado a B: `cambiar-empresa` responde 403.
- Un registro de A no aparece ni puede modificarse desde B.
- Ninguna prueba requiere crear grupos o roles en Entra ID.

## Fuera de alcance

- Gobierno de permisos mediante grupos de Entra.
- Sincronización automática de grupos Microsoft hacia roles ERP.
- Autorización funcional administrada desde Azure/Microsoft 365.
- B2B/B2C para clientes o proveedores externos.
