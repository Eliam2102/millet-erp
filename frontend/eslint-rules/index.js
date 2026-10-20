/** Reglas incrementales del design system; no sustituyen las restricciones de negocio. */
const paleta =
  /\b(bg|text|border|ring|outline|fill|stroke|divide|from|via|to|decoration|placeholder)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}\b/;
const hex = /(bg|text|border|ring|fill|stroke)-\[#[0-9a-fA-F]{3,8}\]/;
const dark = /\bdark:/;
const interno = /\.md\b|\bbackend\b|PLATFORM-TODO|POR CONFIRMAR|\(D\d{2}\)/i;

function enClassName(node) {
  for (let parent = node.parent; parent; parent = parent.parent) {
    if (parent.type === 'JSXAttribute') return parent.name.name === 'className';
    if (parent.type === 'JSXElement' || parent.type === 'Program') return false;
  }
  return false;
}
function regla(pattern, message, { className = false, texto = false } = {}) {
  return {
    meta: { type: 'suggestion', schema: [], messages: { design: message } },
    create(context) {
      const comprobar = (node) => {
        const value = node.type === 'TemplateElement' ? node.value.raw : node.value;
        if (typeof value === 'string' && pattern.test(value) && (!className || enClassName(node))) {
          context.report({ node, messageId: 'design' });
        }
      };
      return texto
        ? { Literal: comprobar, JSXText: comprobar }
        : { Literal: comprobar, TemplateElement: comprobar };
    },
  };
}
export default {
  rules: {
    'no-paleta-default': regla(
      paleta,
      'DESIGN.md §0 y §2: usa tokens semánticos, no la paleta por defecto de Tailwind.',
    ),
    'no-hex': regla(hex, 'DESIGN.md §0 y §2: usa tokens en className, no hex sueltos.', {
      className: true,
    }),
    'no-dark': regla(
      dark,
      'DESIGN.md §2: no hay una paleta dark aprobada; usa los tokens del sistema.',
      { className: true },
    ),
    'no-texto-interno': regla(
      interno,
      'DESIGN.md §6: no muestres rutas, decisiones ni texto interno al usuario.',
      { texto: true },
    ),
  },
};
