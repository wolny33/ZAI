import { useEffect, useRef, useState } from 'react'
import { format } from 'date-fns'
import SeriesMarker from './SeriesMarker.jsx'
import { seriesLabel } from './seriesStyle.js'

/** Table of the measurements: a column per series, a row per moment.
 * A click on a row selects it. */
function MeasurementTable({ series, shapes, rows, selectedKey, onSelect }) {
  const [newestFirst, setNewestFirst] = useState(true)
  const selectedRowRef = useRef(null)
  const ordered = newestFirst ? rows : [...rows].reverse()

  // Shows the selected row, e.g. after a click on a point of the chart
  useEffect(() => {
    const row = selectedRowRef.current
    if (!row) {
      return
    }
    const box = row.closest('.table-scroll')
    const rowEdges = row.getBoundingClientRect()
    // The header covers the top of the box, so the row has to end up below it
    const top = box.querySelector('th').getBoundingClientRect().bottom
    const bottom = box.getBoundingClientRect().top + box.clientHeight
    if (rowEdges.top < top) {
      box.scrollTop -= top - rowEdges.top
    } else if (rowEdges.bottom > bottom) {
      box.scrollTop += rowEdges.bottom - bottom
    }
  }, [selectedKey])

  return (
    <div className="table-frame">
      <div className="table-scroll">
        <table className="table table-sm table-hover align-middle mb-0">
          <caption className="visually-hidden">
            Measurements of the selected series; each column is a series, each row a moment.
          </caption>
          <thead>
            <tr>
              <th scope="col" className="time-cell" aria-sort={newestFirst ? 'descending' : 'ascending'}>
                <button type="button" className="btn btn-link btn-sm p-0 fw-semibold text-reset text-decoration-none"
                  onClick={() => setNewestFirst(!newestFirst)}>
                  Time {newestFirst ? '▼' : '▲'}
                </button>
              </th>
              {series.map((item) => (
                <th key={item.id} scope="col" className="text-end">
                  <SeriesMarker color={item.color} shape={shapes.get(item.id)} />
                  {seriesLabel(item)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {ordered.map((row) => {
              const selected = row.key === selectedKey
              return (
                <tr key={row.key} ref={selected ? selectedRowRef : null} className={selected ? 'table-active' : ''}
                  onClick={() => onSelect(row.key)}>
                  <td className="time-cell">
                    {/* A button, so that the row can also be selected with the keyboard */}
                    <button type="button" className="btn btn-link btn-sm p-0 text-reset text-decoration-none"
                      aria-pressed={selected}>
                      {format(new Date(row.timestamp), 'yyyy-MM-dd HH:mm:ss')}
                    </button>
                  </td>
                  {row.cells.map((measurement, index) => (
                    <td key={series[index].id} className="text-end">{measurement ? measurement.value : '–'}</td>
                  ))}
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}

export default MeasurementTable