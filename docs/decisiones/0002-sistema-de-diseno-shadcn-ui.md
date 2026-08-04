# ADR-0002: Sistema de diseño basado en shadcn/ui

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: frontend, diseño, fundación

## Contexto y problema

El frontend del ERP tendrá decenas de pantallas a lo largo de los módulos
(facturación, cobranza, compras, almacén, contabilidad, etc.). Sin un sistema
de diseño definido desde el inicio, cada pantalla acaba con su propio estilo,
los componentes se duplican, y la accesibilidad queda al azar. Necesitamos:

- Componentes consistentes en toda la aplicación
- Accesibilidad (WCAG AA mínimo) sin tener que reimplementarla
- Velocidad de desarrollo: no perder tiempo construyendo desde cero un menú,
  un dialog o una tabla
- Capacidad de personalizar visualmente sin pelear con el framework

## Drivers de la decisión

- TypeScript-first
- Componentes accesibles por defecto
- Personalización profunda sin fork
- Stack ligero (no agregar 200KB de framework de UI)
- Comunidad activa y documentación al día

## Opciones consideradas

1. shadcn/ui (Radix UI primitives + Tailwind CSS)
2. Material UI (MUI)
3. Ant Design
4. Chakra UI / Mantine
5. Construir un design system propio desde cero

## Decisión

Se adopta **shadcn/ui** como base del sistema de diseño, usando **Tailwind CSS**
para estilos y **Radix UI** como capa de primitivos accesibles (encapsulados por
shadcn).

A diferencia de las librerías tradicionales, shadcn/ui **no se instala como
dependencia npm**: se copia el código fuente de cada componente al repositorio
del proyecto. Los componentes son nuestros y los podemos modificar libremente.

Sobre esa base se construirá una capa de componentes específicos del dominio
del ERP (`MoneyInput`, `RfcInput`, `FechaPicker`, `TablaDeCartera`, etc.) en
`frontend/src/components/erp/`.

## Consecuencias

**Positivas**
- Control total sobre el código de los componentes (no dependencia de upgrades de tercero)
- Bundle size mínimo: solo se incluye lo que se usa
- Accesibilidad heredada de Radix sin trabajo extra
- Tailwind permite cambios visuales rápidos sin tocar componentes
- Convención muy popular en el ecosistema React 2024-2026; fácil contratar

**Negativas**
- Cada componente vive en el repo: las "actualizaciones" requieren correr el CLI de shadcn manualmente
- Curva de aprendizaje de Tailwind para devs que vienen de CSS o styled-components
- Más configuración inicial que MUI (donde todo viene "de fábrica")

## Descartadas

**Material UI**. Diseño Material no encaja con la estética típica de un ERP
empresarial mexicano y pelearse con el theme system para "des-materializarlo"
es trabajo perdido.

**Ant Design**. Excelente para dashboards corporativos chinos pero su lenguaje
visual es muy específico y los componentes son difíciles de personalizar a
profundidad sin escribir mucho CSS sobre-especificado.

**Chakra / Mantine**. Buenas opciones, pero la traction de shadcn/ui en 2025-2026
es mayor; mejor ecosistema de componentes derivados (formularios complejos,
tablas con TanStack, etc.).

**Construir desde cero**. Imposible de justificar: dos meses de trabajo para
recrear lo que ya existe accesible y probado.

## Notas de implementación

- **Versiones iniciales**: React 19 + Tailwind v4. shadcn/ui CLI ya soporta ambas por default desde fines de 2024. Si en el futuro algún plugin específico no soporta v4, se evalúa puntualmente
- Inicializar shadcn/ui en `frontend/` con `npx shadcn@latest init`
- Configurar Tailwind con tokens del proyecto (colores corporativos de Millet, tipografía)
- Crear estructura: `frontend/src/components/ui/` (shadcn nativos) y `frontend/src/components/erp/` (compuestos del dominio)
- ADRs hijo posibles: tokens de diseño (colores, tipografía, espaciado), componentes de formularios, estrategia de tablas grandes (TanStack Table)
