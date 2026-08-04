import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  // El routeTree.gen.ts es generado por @tanstack/router-plugin; no debe
  // ser linteado. <c>coverage/</c> es output del v8 reporter de Vitest
  // (HTML + assets minificados) — no es código nuestro.
  globalIgnores(['dist', 'coverage', 'src/routeTree.gen.ts']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  // Las rutas de TanStack Router exportan un objeto `Route` además del
  // componente; el rule react-refresh/only-export-components flaggearía
  // esto pero es el patrón canónico de file-based routing.
  {
    files: ['src/routes/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
  // Los componentes de shadcn/ui (en src/components/ui/) son código
  // generado por la CLI de shadcn y siguen un patrón canónico que
  // mezcla en un mismo archivo: (1) re-exports directos de primitivos
  // de Radix (DropdownMenu, Tooltip, etc.), (2) componentes wrapper, y
  // (3) helpers como `buttonVariants` (cva). La regla
  // react-refresh/only-export-components flaggea (1) y (3) como falsos
  // positivos. Refactorizar contra el patrón shadcn/ui implicaría
  // mantener un fork del scaffold; preferimos desactivar la regla aquí.
  {
    files: ['src/components/ui/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
  // Centros de Costo: el helper de features/centros-costo/lib/etiquetas.ts
  // es el ÚNICO punto de traducción Dim↔etiqueta (07 §0 del módulo — "no
  // strings sueltos"). Los literales de vocabulario contextual quedan
  // prohibidos en el resto del src; CI corre lint, así que esto es
  // enforcement real. El test del helper valida CONTRA el helper, por eso
  // también queda exento.
  {
    files: ['src/**/*.{ts,tsx}'],
    ignores: [
      'src/features/centros-costo/lib/etiquetas.ts',
      'src/features/centros-costo/lib/etiquetas.test.ts',
    ],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector:
            "Literal[value=/Dimensi[oó]n [123]|Grupo dimensi[oó]n [23]/]",
          message:
            'Vocabulario Dim↔etiqueta fuera del helper único: usa etiquetaNivel()/etiquetaTipoNodo() de features/centros-costo/lib/etiquetas.ts (07 §0).',
        },
        {
          selector:
            "TemplateElement[value.raw=/Dimensi[oó]n [123]|Grupo dimensi[oó]n [23]/]",
          message:
            'Vocabulario Dim↔etiqueta fuera del helper único: usa etiquetaNivel()/etiquetaTipoNodo() de features/centros-costo/lib/etiquetas.ts (07 §0).',
        },
        // El texto de JSX (<span>Dimensión 1</span>) es nodo JSXText, no
        // Literal — sin este selector el hueco más común quedaría abierto
        // (verificado al armar la regla: Literal y TemplateElement no lo
        // cachan).
        {
          selector: "JSXText[value=/Dimensi[oó]n [123]|Grupo dimensi[oó]n [23]/]",
          message:
            'Vocabulario Dim↔etiqueta fuera del helper único: usa etiquetaNivel()/etiquetaTipoNodo() de features/centros-costo/lib/etiquetas.ts (07 §0).',
        },
        // Fecha local vs UTC (ADR-0013/0040). `new Date().toISOString()`
        // truncado a fecha da la fecha UTC y adelanta el día después de las
        // 18:00 hora de México → corrimiento en defaults DateOnly, filtros de
        // reportes y conciliación. Usar hoyLocalISO() de @/lib/datetime.
        //
        // OJO: estos selectores viven en ESTE bloque (no en uno nuevo) a
        // propósito: en flat config, `no-restricted-syntax` NO se fusiona
        // entre config objects — el último que aplique a un archivo REEMPLAZA
        // al anterior. Un bloque nuevo con esta regla apagaría la de
        // centros-costo para todo src/. Van juntos aquí.
        // Solo `new Date()` SIN argumentos (el "ahora"): su hora puede cruzar
        // la medianoche en UTC. `new Date(y, m, d)` construye medianoche local
        // y en México (UTC-6) no cruza el día → ése NO se restringe.
        {
          selector:
            "CallExpression[callee.property.name='slice'][callee.object.callee.property.name='toISOString'][callee.object.callee.object.type='NewExpression'][callee.object.callee.object.callee.name='Date'][callee.object.callee.object.arguments.length=0]",
          message:
            'Fecha UTC: `new Date().toISOString().slice(...)` adelanta el día tras las 18:00 en México. Usa hoyLocalISO() de @/lib/datetime.',
        },
        {
          selector:
            "CallExpression[callee.property.name='split'][callee.object.callee.property.name='toISOString'][callee.object.callee.object.type='NewExpression'][callee.object.callee.object.callee.name='Date'][callee.object.callee.object.arguments.length=0]",
          message:
            'Fecha UTC: `new Date().toISOString().split(...)` adelanta el día tras las 18:00 en México. Usa hoyLocalISO() de @/lib/datetime.',
        },
        // Idempotency-Key con sufijo (ADR-0020). El key DEBE ser un UUID v4
        // puro; concatenarle un sufijo (índice, contador, `-submit`) lo hace
        // inválido → 400. Bug que reincidió dos veces (#564 CxC, #566 CxP).
        // Para multi-submit usa `crypto.randomUUID()` fresco por submit; para
        // dinero reintentable usa useBodyScopedIdempotencyKey(). Cachan solo
        // el sufijo INLINE (no vía variable), que es la forma en que apareció.
        {
          selector: "Property[key.name='idempotencyKey'] > TemplateLiteral",
          message:
            'Idempotency-Key debe ser UUID v4 puro, sin sufijos (bug #564/#566). Usa crypto.randomUUID() o useBodyScopedIdempotencyKey().',
        },
        {
          selector: "Property[key.name='idempotencyKey'] > BinaryExpression",
          message:
            'Idempotency-Key debe ser UUID v4 puro, sin concatenación (bug #564/#566). Usa crypto.randomUUID() o useBodyScopedIdempotencyKey().',
        },
      ],
    },
  },
])
