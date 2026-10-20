/** Reglas incrementales del design system; no sustituyen las restricciones de negocio. */
const paleta =
  /\b(bg|text|border|ring|outline|fill|stroke|divide|from|via|to|decoration|placeholder)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}\b/;
const hex = /(bg|text|border|ring|fill|stroke)-\[#[0-9a-fA-F]{3,8}\]/;
const dark = /\bdark:/;
const interno = /\.md\b|\bbackend\b|PLATFORM-TODO|POR CONFIRMAR|\(D\d{2}\)|\bADR-?\d|\bstub\b/i;
const marcador = /^\[[A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑ0-9 _/.-]{2,}\]$/;

// Solo se excluyen posiciones técnicas del AST, nunca identificadores de pantalla.
function textoTecnico(node, value) {
  if (/^(?:https?:\/\/|\/api\/)/.test(value)) return true;
  for (let child = node, parent = node.parent; parent; child = parent, parent = parent.parent) {
    if (parent.type === 'JSXAttribute') {
      const name = parent.name.name;
      return (
        name === 'className' ||
        name === 'key' ||
        /^data-/.test(name) ||
        (/^aria-/.test(name) && !['aria-label', 'aria-description'].includes(name)) ||
        (name === 'value' && parent.parent.name?.name === 'SelectItem')
      );
    }
    if (parent.type === 'Property') {
      if (parent.key === child && !parent.computed) return true;
      if (parent.key.name === 'queryKey') return true;
    }
    if (parent.type === 'MemberExpression' && parent.property === child) return true;
    if (parent.type === 'CallExpression') {
      const name = parent.callee.name ?? parent.callee.property?.name;
      if (
        ['apiRequest', 'apiFetch', 'fetch', 'URL'].includes(name) &&
        parent.arguments[0] === child
      )
        return true;
    }
    if (
      parent.type === 'NewExpression' &&
      parent.callee.name === 'URL' &&
      parent.arguments[0] === child
    )
      return true;
    if (parent.type === 'JSXElement' || parent.type === 'Program') break;
  }
  return false;
}

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
        if (typeof value !== 'string') return;
        if (texto && textoTecnico(node, value)) return;
        if (
          (pattern.test(value) ||
            (texto && (marcador.test(value.trim()) || /\bTODO\b/.test(value)))) &&
          (!className || enClassName(node))
        ) {
          context.report({ node, messageId: 'design' });
        }
      };
      return texto
        ? { Literal: comprobar, JSXText: comprobar, TemplateElement: comprobar }
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
