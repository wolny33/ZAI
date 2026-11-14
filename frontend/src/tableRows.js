/**
 * Builds the table rows: one row per second, one cell per series. When a series has several
 * measurements in the same second, extra rows with the same time hold them. Keeps the order of
 * the measurements (newest first).
 */
export function tableRows(series, measurements) {
  // second (in ms) -> (series id -> its measurements in that second)
  const moments = new Map()
  for (const measurement of measurements) {
    // The table shows the time to the second, so measurements within one second share a row
    const moment = Math.floor(Date.parse(measurement.timestamp) / 1000) * 1000
    const bySeries = moments.get(moment) ?? new Map()
    bySeries.set(measurement.seriesId, [...(bySeries.get(measurement.seriesId) ?? []), measurement])
    moments.set(moment, bySeries)
  }

  const rows = []
  for (const [moment, bySeries] of moments) {
    const depth = Math.max(...[...bySeries.values()].map((list) => list.length))
    for (let index = 0; index < depth; index++) {
      const cells = series.map((item) => bySeries.get(item.id)?.[index] ?? null)
      rows.push({ key: `${moment}#${index}`, timestamp: moment, cells })
    }
  }
  return rows
}