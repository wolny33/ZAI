// SVG outline of each shape in a 10 x 10 box, drawn like the Chart.js point styles
const PATHS = {
  circle: 'M5 0.5a4.5 4.5 0 1 1 0 9a4.5 4.5 0 1 1 0-9z',
  triangle: 'M5 0.5L9.5 9.5H0.5z',
  rect: 'M1 1H9V9H1z',
  rectRot: 'M5 0L10 5L5 10L0 5z',
  star: 'M5 0.5V9.5M0.5 5H9.5M1.8 1.8L8.2 8.2M8.2 1.8L1.8 8.2',
  crossRot: 'M1 1L9 9M9 1L1 9',
}

/** Small colored mark of a series, with the same shape as its points on the chart. */
function SeriesMarker({ color, shape }) {
  // These shapes are made of lines only, so they have nothing to fill
  const lineOnly = shape === 'crossRot' || shape === 'star'
  return (
    <svg className="series-marker" viewBox="0 0 10 10" aria-hidden="true">
      <path d={PATHS[shape]} fill={lineOnly ? 'none' : color} stroke={color} strokeWidth={lineOnly ? 2 : 1} />
    </svg>
  )
}

export default SeriesMarker