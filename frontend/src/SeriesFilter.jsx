import SeriesMarker from './SeriesMarker.jsx'
import { seriesLabel } from './seriesStyle.js'
import { PRESETS } from './timeRange.js'

/** A date with an optional time (the time field can be used once a date is chosen). */
function DateTimeField({ id, label, range, onRangeChange, dateKey, timeKey }) {
  const change = (key, value) => onRangeChange({ ...range, preset: 'custom', [key]: value })
  return (
    <div className="mb-2">
      <label htmlFor={id} className="form-label small mb-1">{label}</label>
      <div className="d-flex gap-2">
        <input id={id} type="date" className="form-control form-control-sm range-date" value={range[dateKey]}
          onChange={(event) => change(dateKey, event.target.value)} />
        <input type="time" className="form-control form-control-sm range-time" aria-label={`${label} time (optional)`}
          value={range[timeKey]} disabled={!range[dateKey]} onChange={(event) => change(timeKey, event.target.value)} />
      </div>
    </div>
  )
}

/** Filters of the view: time range (a preset or own dates) and the visible series. */
function SeriesFilter({ series, seriesLoaded, shapes, selectedIds, onSelectedIdsChange, range, onRangeChange }) {
  function toggleSeries(id) {
    onSelectedIdsChange(selectedIds.includes(id) ? selectedIds.filter((other) => other !== id) : [...selectedIds, id])
  }

  return (
    <section className="card border-0 shadow-sm rounded-4" aria-labelledby="filters-title">
      <div className="card-body p-4">
        <h2 id="filters-title" className="h5 mb-3">Filters</h2>

        <fieldset className="mb-4">
          <legend className="form-label fs-6 fw-semibold">Time range</legend>
          <div className="btn-group w-100 mb-3" role="group" aria-label="Quick time ranges">
            {PRESETS.map((preset) => (
              <button
                key={preset.id}
                type="button"
                className={`btn btn-sm ${range.preset === preset.id ? 'btn-primary' : 'btn-outline-primary'}`}
                aria-pressed={range.preset === preset.id}
                onClick={() => onRangeChange({ preset: preset.id, fromDate: '', fromTime: '', toDate: '', toTime: '' })}
              >
                {preset.label}
              </button>
            ))}
          </div>

          <DateTimeField id="range-from" label="From" range={range} onRangeChange={onRangeChange}
            dateKey="fromDate" timeKey="fromTime" />
          <DateTimeField id="range-to" label="To" range={range} onRangeChange={onRangeChange}
            dateKey="toDate" timeKey="toTime" />
        </fieldset>

        <fieldset>
          <legend className="form-label fs-6 fw-semibold">Series</legend>
          <div className="d-flex gap-2 mb-2">
            <button type="button" className="btn btn-link btn-sm p-0"
              onClick={() => onSelectedIdsChange(series.map((item) => item.id))}>All</button>
            <button type="button" className="btn btn-link btn-sm p-0" onClick={() => onSelectedIdsChange([])}>None</button>
          </div>
          {!seriesLoaded && <p role="status" className="small text-body-secondary mb-0">Loading…</p>}
          {seriesLoaded && series.length === 0 && <p className="small mb-0">No series yet.</p>}
          {series.map((item) => (
            <div key={item.id} className="form-check">
              <input
                id={`series-${item.id}`}
                type="checkbox"
                className="form-check-input"
                checked={selectedIds.includes(item.id)}
                onChange={() => toggleSeries(item.id)}
              />
              <label htmlFor={`series-${item.id}`} className="form-check-label d-flex align-items-start gap-2">
                <SeriesMarker color={item.color} shape={shapes.get(item.id)} />
                {seriesLabel(item)}
              </label>
            </div>
          ))}
        </fieldset>
      </div>
    </section>
  )
}

export default SeriesFilter