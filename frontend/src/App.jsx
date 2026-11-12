import { useEffect, useMemo, useState } from 'react'
import { getMeasurements, getSeries, MEASUREMENT_LIMIT } from './api.js'
import MeasurementChart from './MeasurementChart.jsx'
import SeriesFilter from './SeriesFilter.jsx'
import { assignShapes } from './seriesStyle.js'
import { rangeDates } from './timeRange.js'

function App() {
  const [series, setSeries] = useState([])
  const [seriesLoaded, setSeriesLoaded] = useState(false)
  const [seriesError, setSeriesError] = useState(null)
  const [selectedIds, setSelectedIds] = useState([])
  const [range, setRange] = useState({ preset: 'all', from: '', to: '' })
  // The last answer for the measurements, together with the filters it was asked for
  const [result, setResult] = useState(null)

  useEffect(() => {
    let ignore = false
    getSeries()
      .then((data) => {
        if (!ignore) {
          setSeries(data)
          setSelectedIds(data.map((item) => item.id))
          setSeriesLoaded(true)
        }
      })
      .catch((err) => {
        if (!ignore) {
          setSeriesError(err.message)
          setSeriesLoaded(true)
        }
      })
    return () => {
      ignore = true
    }
  }, [])

  const rangeError = rangeDates(range).error
  // Kept between renders, so that the chart is updated only when they really change
  const shapes = useMemo(() => assignShapes(series), [series])
  // All units in the order of the series ('' for no unit); each unit gets its own y axis
  const units = useMemo(() => [...new Set(series.map((item) => item.unit ?? ''))], [series])

  useEffect(() => {
    const { from, to, error } = rangeDates(range)
    if (selectedIds.length === 0 || error) {
      return
    }
    let ignore = false
    getMeasurements(selectedIds, from, to)
      .then((data) => {
        if (!ignore) {
          setResult({ selectedIds, range, measurements: data, error: null })
        }
      })
      .catch((err) => {
        if (!ignore) {
          setResult({ selectedIds, range, measurements: [], error: err.message })
        }
      })
    return () => {
      ignore = true
    }
  }, [selectedIds, range])

  // The answer is current only if it was asked for exactly these filters
  const current = result?.selectedIds === selectedIds && result?.range === range
  const loading = selectedIds.length > 0 && !rangeError && !current
  // The chart keeps the last good answer while new data loads, so it changes smoothly
  const shown = result && !result.error ? result : null
  const shownSeries = useMemo(
    () => (shown ? series.filter((item) => shown.selectedIds.includes(item.id)) : []),
    [series, shown],
  )
  const showChart = selectedIds.length > 0 && !rangeError && shown?.measurements.length > 0

  return (
    <>
      <header className="app-header">
        <div className="container d-flex align-items-center gap-2 py-3">
          <svg className="app-logo" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"
            strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M3 3v18h18" />
            <path d="M7 15l4-5 3 3 5-7" />
          </svg>
          <h1 className="app-title">Measurement series</h1>
        </div>
      </header>

      <main className="container py-4">
        {/* Without the series there is nothing to filter or show */}
        {seriesError && <div className="alert alert-danger" role="alert">{seriesError}</div>}

        {!seriesError && (
          <div className="dashboard">
            <SeriesFilter series={series} seriesLoaded={seriesLoaded} shapes={shapes} selectedIds={selectedIds}
              onSelectedIdsChange={setSelectedIds} range={range} onRangeChange={setRange} />

            <section className="card border-0 shadow-sm rounded-4" aria-labelledby="chart-title">
              <div className="card-body p-4">
                <div className="d-flex align-items-center justify-content-between mb-3">
                  <h2 id="chart-title" className="h5 mb-0">Chart</h2>
                  {loading && <span role="status" className="small text-body-secondary">Loading…</span>}
                </div>
                {rangeError && <div className="alert alert-warning mb-0" role="alert">{rangeError}</div>}
                {!seriesLoaded && <p role="status" className="text-body-secondary mb-0">Loading…</p>}
                {seriesLoaded && !rangeError && selectedIds.length === 0 && <p className="mb-0">Select at least one series.</p>}
                {current && result.error && <div className="alert alert-danger mb-0" role="alert">{result.error}</div>}
                {current && !result.error && result.measurements.length === 0 && (
                  <p className="mb-0">No measurements in this time range.</p>
                )}
                {showChart && <MeasurementChart series={shownSeries} shapes={shapes} units={units} measurements={shown.measurements} />}
                {showChart && shown.measurements.length === MEASUREMENT_LIMIT && (
                  <p className="small text-body-secondary mt-2 mb-0">
                    Showing the latest {MEASUREMENT_LIMIT} measurements, narrow the time range to see older ones.
                  </p>
                )}
              </div>
            </section>
          </div>
        )}
      </main>
    </>
  )
}

export default App
