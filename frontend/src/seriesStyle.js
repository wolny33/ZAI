// Point shapes that tell the series apart not only by color. The names are Chart.js point styles,
// stored in the icon field of a series.
export const SHAPES = ['circle', 'triangle', 'rect', 'rectRot', 'star', 'crossRot']

/**
 * Gives every series a shape: its icon if it is a known shape, otherwise the next shape no other
 * series uses; when all shapes are taken, they repeat. Returns a Map from the series id to its shape.
 */
export function assignShapes(seriesList) {
  const chosen = new Set(seriesList.map((series) => series.icon).filter((icon) => SHAPES.includes(icon)))
  const free = SHAPES.filter((shape) => !chosen.has(shape))
  const shapes = new Map()
  let next = 0
  for (const series of seriesList) {
    if (SHAPES.includes(series.icon)) {
      shapes.set(series.id, series.icon)
    } else {
      // Unused shapes first, then all shapes in turn
      shapes.set(series.id, next < free.length ? free[next] : SHAPES[(next - free.length) % SHAPES.length])
      next += 1
    }
  }
  return shapes
}

/** Returns the series name with its unit, e.g. "Living room temperature [°C]". */
export function seriesLabel(series) {
  return series.unit ? `${series.name} [${series.unit}]` : series.name
}