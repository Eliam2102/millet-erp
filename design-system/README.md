# Design system Millet — catálogo vivo

El frontend integra los tokens de Millet y los componentes compartidos de
`src/components/erp` y `src/components/ui`. La guía normativa es [DESIGN.md](./DESIGN.md);
[FIGMA.md](./FIGMA.md) conserva la referencia visual y las diferencias verificadas.

## Estado actual

Migrados al patrón compartido: **Facturación, CxC, Tesorería, Contabilidad y
Comercio exterior**. Sus pantallas usan componentes ERP y sus carpetas tienen
las cuatro reglas `ds/*` con severidad error. Los demás módulos continúan en curso.
Esta clasificación describe la migración en código; no acredita aceptación del
cliente ni una comparación visual completa de todas las pantallas.

El catálogo `/sistema-diseno` documenta 30 componentes con propósito, límites,
props, reglas, demostración y código copiable. El código mostrado es el mismo
archivo TSX que se ejecuta en cada ejemplo. Incluye fundamentos, plantillas 5.1
y 5.2 y reglas de contenido. Los datos están marcados como ficticios; los ejemplos
no llaman a la API. La caché aislada de ContextoSucursal sustituye los nombres
sin modificar la sesión ni compartir datos con el QueryClient de la aplicación.

## Cómo entrar

Inicia sesión con una cuenta que tenga `admin.parametros.editar` y abre
`/sistema-diseno` dentro del ERP. Se reutiliza ese permiso administrativo existente,
sin agregar permisos al servidor. La guarda de ruta y la navegación lo verifican;
ser usuario de un módulo o tener sólo lectura de parámetros no permite entrar.

## Cómo hacer una pantalla nueva en 6 pasos

1. **Plantilla:** elige listado §5.1 o detalle §5.2 y consulta el catálogo antes
   de crear componentes. El AppShell ya aporta los márgenes de página.
2. **PageHeader:** título, descripción de una oración y una acción primaria por
   zona; `mono` para folios.
3. **ListadoModulo o detalle 5.2:** integra tabs, filtros, tabla y paginación de
   10 filas; diseña vacío, carga y error. En detalle separa datos, validación,
   partidas, ResumenImportes y actividad.
4. **FormGrid y tamaños de modal:** declara spans 3/4/6/12 y usa FormSeccion para
   alinear secciones. DialogContent: sm/md/lg; SheetContent: form/md/lg.
   Usa lg para partidas, importes o dos columnas; DialogBody/SheetBody para scroll.
5. **Mapa de estados:** conserva el estado del dominio y asigna texto y tono
   semántico estable. EstadoBadge sólo cubre Compras; otras entidades usan Badge
   con su mapa. Las acciones deshabilitadas siempre explican el motivo.
6. **Lint `ds/*` en verde:** verifica tipos, lint y pruebas afectadas; revisa
   visualmente la pantalla y su navegación por teclado antes de entregarla.

## Tokens y compatibilidad

Tailwind 4 expone los tokens mediante `@theme inline` en `frontend/src/index.css`,
que importa `tokens.css`. `tailwind.config.ts` es referencia; no se copia sobre
el proyecto. Al cambiar un token, sincroniza JSON, CSS, referencia y adaptación.
IBM Plex Sans es la fuente UI (13 px base); IBM Plex Mono se usa para folios.
El modo oscuro heredado no tiene paleta aprobada y no se declara migrado.

Dialog sin size y Sheet wide/legacy son compatibilidad heredada, no recetas para
pantallas nuevas. La documentación muestra sus APIs reales. La búsqueda global
de registros/acciones, Inicio completo y composición móvil siguen en curso.

## Verificación local

Desde `frontend/`:

```sh
npx tsr generate
npx tsc -b tsconfig.app.json tsconfig.node.json
npx eslint src/features/sistema-diseno src/routes/_app/sistema-diseno
npx vitest run src/features/sistema-diseno src/components src/lib
```

Las pruebas del catálogo cubren secciones, bloqueo de acceso, ausencia de red,
filtrado, paginación, estados de carga/error y copia del código.
