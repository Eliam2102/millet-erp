# Design system Millet — integración

Los cuatro archivos aportados (`DESIGN.md`, `tokens.json`, `tokens.css` y
`tailwind.config.ts`) se conservan como referencia visual versionada. Empieza por
leer `DESIGN.md` completo. El canvas mencionado en la guía no está incluido en
esta entrega; la comparación con sus 12 pantallas queda pendiente.

## Uso en este repositorio

El frontend usa Tailwind 4. `frontend/src/index.css` importa `tokens.css` en la
capa `base` y expone las clases del config adjunto mediante `@theme inline`:
`bg-surface-page`, `text-ink`, `border-line-control`, `bg-brand`, `h-ctl-xl`,
`w-rail`, `shadow-card`, etc. Los aliases shadcn existentes siguen funcionando.
La adaptación usa los valores hex exactos de los tokens para esos aliases;
no mezcla el formato HSL del adjunto con los colores completos de Tailwind 4.

La fuente se carga desde Google Fonts según `tokens.css`, con fallback local.
El modo oscuro anterior se conserva como compatibilidad: **no tiene una paleta
aprobada en estos adjuntos y no se declara migrado**.

Los tokens se importan en una capa para que clases como `text-white` y tamaños
explícitos de componentes mantengan prioridad sobre los estilos base del adjunto.
Al cambiar un token, actualiza JSON, CSS, la referencia Tailwind y la adaptación
`@theme` cuando corresponda.

## Primera etapa

- `Button`: acción primaria, secundaria/outline, ghost, destructive,
  `secondary-danger` y link; tamaños default 34px, sm 28px, lg 36px e icon 34px.
  Se conservan las props y nombres ya utilizados por las pantallas.
- `Input`, `Textarea`, `Select` y `Label`: superficies, bordes, tipografía y foco.
- `Card`: radios de 10px, sombra de tarjeta y padding/títulos de la guía.
- `Badge`: variantes semánticas; los nombres anteriores siguen disponibles.
- `EstadoBadge`: Compras reutiliza las recetas del badge sin cambiar sus labels,
  glosarios, estados de dominio ni atributos de identificación.

Ejemplo dentro de una pantalla existente:

```tsx
<Label htmlFor="referencia">Referencia</Label>
<Input id="referencia" />
<Button variant="secondary">Guardar borrador</Button>
<Badge variant="warning">Pendiente</Badge>
```

Los controles interactivos tienen un mínimo de 44px en móvil. Usa `size="lg"`
en acciones de formularios/sheets y da `aria-label` al usar `size="icon"`.
Para una acción deshabilitada, explica el bloqueo en texto cercano o tooltip.

Las clases específicas que las pantallas ya pasan con `className` pueden
sobrescribir la receta compartida; migrarlas requiere revisar cada consumidor.
Pendientes: shell/rail/topbar, tablas, dialogs/sheets, los demás componentes y
pantallas, colores locales heredados y revisión contra el canvas. Esta etapa
no cambia reglas funcionales, paginación, prioridades ni permisos.

Las instrucciones para agentes están en `frontend/AGENTS.md` y `CLAUDE.md`.
