import { useEffect, useRef, useState } from 'react'
import { format } from 'date-fns'
import SeriesMarker from './SeriesMarker.jsx'
import { seriesLabel } from './seriesStyle.js'

const PAGE_SIZE = 100

/** Table of the measurements: a column per series, a row per moment; a click on a row selects it. */
function MeasurementTable({ series, shapes, rows, selectedKey, onSelect }) {
  const [newestFirst, setNewestFirst] = useState(true)
  const [page, setPage] = useState(0)
  // The selection that the page was last matched to
  const [followedKey, setFollowedKey] = useState(selectedKey)
  const scrollRef = useRef(null)
  const selectedRowRef = useRef(null)
  const ordered = newestFirst ? rows : [...rows].reverse()

  // A new selection opens the page with its row
  if (selectedKey !== followedKey) {
    setFollowedKey(selectedKey)
    const index = ordered.findIndex((row) => row.key === selectedKey)
    if (index >= 0) {
      setPage(Math.floor(index / PAGE_SIZE))
    }
  }

  // New data can have fewer pages than the page that was open
  const lastPage = Math.max(0, Math.ceil(ordered.length / PAGE_SIZE) - 1)
  const shownPage = Math.min(page, lastPage)
  const first = shownPage * PAGE_SIZE
  const visible = ordered.slice(first, first + PAGE_SIZE)

  useEffect(() => {
    scrollRef.current.scrollTop = 0
  }, [shownPage])

  // Shows the selected row, e.g. after a click on a point of the chart
  useEffect(() => {
    const row = selectedRowRef.current
    if (!row) {
      return
    }
    const box = scrollRef.current
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
    <>
      <div className="table-frame">
        <div className="table-scroll" ref={scrollRef}>
          <table className="table table-sm table-hover align-middle mb-0">
            <caption className="visually-hidden">
              Measurements of the selected series; each column is a series, each row a moment.
            </caption>
            <thead>
              <tr>
                <th scope="col" className="time-cell" aria-sort={newestFirst ? 'descending' : 'ascending'}>
                  <button type="button" className="btn btn-link btn-sm p-0 fw-semibold text-reset text-decoration-none"
                    onClick={() => { setNewestFirst(!newestFirst); setPage(0) }}>
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
              {visible.map((row) => {
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

      {lastPage > 0 && (
        <nav className="table-pages" aria-label="Table pages">
          <button type="button" className="btn btn-outline-primary btn-sm" disabled={shownPage === 0}
            onClick={() => setPage(shownPage - 1)}>
            Previous
          </button>
          <span className="small text-body-secondary" aria-live="polite">
            Rows {first + 1}–{first + visible.length} of {ordered.length}
          </span>
          <button type="button" className="btn btn-outline-primary btn-sm" disabled={shownPage === lastPage}
            onClick={() => setPage(shownPage + 1)}>
            Next
          </button>
        </nav>
      )}
    </>
  )
}

export default MeasurementTable