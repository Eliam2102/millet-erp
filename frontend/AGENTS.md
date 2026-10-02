# UI del ERP Millet

Antes de crear o modificar una pantalla o componente, lee completo
`../design-system/DESIGN.md`, consulta los nodos de referencia en
`../design-system/FIGMA.md` y después `../design-system/README.md` (rutas desde
esta carpeta). La guía visual no cambia permisos, reglas de negocio ni estados
reales del backend.

- Reutiliza `src/components/ui/` y los componentes ERP existentes antes de crear uno nuevo.
- Usa los tokens de `../design-system/tokens.json`, expuestos en `src/index.css`.
  No agregues hex sueltos ni colores de la paleta default de Tailwind en UI nueva
  o migrada. Si falta un token, propón su incorporación a la guía y los tokens.
- El frontend usa **Tailwind 4**: la configuración efectiva es `@theme inline`
  en `src/index.css`. El `tailwind.config.ts` adjunto es referencia; no lo copies
  sobre el proyecto ni instales `tailwindcss-animate` (ya existe `tw-animate-css`).
- Usa Plex Sans, cuerpo `text-sm` (13px), Plex Mono para folios/UUID/RFC y
  `tabular-nums` para importes. Las tarjetas usan `shadow-card` o `shadow-card-flat`.
- Usa `Badge` con `success`, `warning`, `danger`, `info` o `neutral` para estados;
  conserva texto visible. Compras usa `EstadoBadge` con sus estados y glosarios.
- Mantén labels asociados a campos, foco visible y `aria-label` en botones de
  ícono. Explica el motivo de acciones deshabilitadas con texto o tooltip.
- Las pantallas nuevas parten de las plantillas de la sección 5 y pasan el
  checklist de la sección 9. En una migración parcial, identifica lo pendiente.
- Verifica build y lint, las pruebas afectadas y la pantalla en navegador antes
  de afirmar que la migración visual está completa. Preserva impresión, teclado,
  formularios, navegación y controles de acceso.
