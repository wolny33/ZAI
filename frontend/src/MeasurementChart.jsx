import { useEffect, useRef } from 'react'
import { Chart, Legend, LinearScale, LineController, LineElement, PointElement, TimeScale, Tooltip } from 'chart.js'
import 'chartjs-adapter-date-fns'
import { seriesLabel } from './seriesStyle.js'

// Only the parts of Chart.js that a line chart over time needs
Chart.register(LineController, LineElement, PointElement, LinearScale, TimeScale, Tooltip, Legend)

/**
 * Builds one dataset per series (points from the oldest) and one y axis per unit. The axes follow
 * the order of allUnits, so each unit keeps its axis and side when other series are shown or hidden.
 */
function chartData(series, shapes, measurements, allUnits) {
  // Smaller points when there are many of them
  const pointRadius = measurements.length > 300 ? 2 : 3
  const axisId = (unit) => `y${allUnits.indexOf(unit ?? '')}`

  // Series without measurements in the range get no line and no axis
  const seriesWithData = new Set(measurements.map((measurement) => measurement.seriesId))
  const withData = series.filter((item) => seriesWithData.has(item.id))

  const datasets = withData.map((item) => ({
    seriesId: item.id,
    label: seriesLabel(item),
    unit: item.unit,
    // The API sends the newest first, a line needs the points in time order
    data: measurements
      .filter((measurement) => measurement.seriesId === item.id)
      .map((measurement) => ({ x: new Date(measurement.timestamp), y: measurement.value }))
      .reverse(),
    borderColor: item.color,
    backgroundColor: item.color,
    pointStyle: shapes.get(item.id),
    pointRadius,
    pointHoverRadius: 6,
    borderWidth: 2,
    yAxisID: axisId(item.unit),
  }))

  const scales = {
      x: {
      type: 'time',
      time: {
        tooltipFormat: 'yyyy-MM-dd HH:mm',
        // 24-hour clock and day before month, e.g. "14:00" and "3 Nov"
        displayFormats: { second: 'HH:mm:ss', minute: 'HH:mm', hour: 'HH:mm', day: 'd MMM', week: 'd MMM' },
      },
            ticks: {
        maxRotation: 0,
        autoSkipPadding: 12,
        // Midnight is a major tick: it shows the date in bold and is kept when labels are skipped
        major: { enabled: true },
        font: (context) => (context.tick?.major ? { weight: 'bold' } : {}),
      }
    }
  }
  // Only the units of the drawn series get an axis
  const units = allUnits.filter((unit) => withData.some((item) => (item.unit ?? '') === unit))
  units.forEach((unit, index) => {
    scales[axisId(unit)] = {
      type: 'linear',
      // Units in turn on the left and on the right
      position: allUnits.indexOf(unit) % 2 === 0 ? 'left' : 'right',
      title: { display: true, text: unit ? `[${unit}]` : '[no unit]' },
      // Grid lines only for the first shown axis, so that they do not overlap
      grid: { drawOnChartArea: index === 0 },
    }
  })
  return { datasets, scales }
}

/** Line chart of the measurements, one line per series, with time on the x axis. */
function MeasurementChart({ series, shapes, units, measurements }) {
  const canvasRef = useRef(null)
  const chartRef = useRef(null)

  // Creates the chart once and removes it when the component is gone
  useEffect(() => {
    const chart = new Chart(canvasRef.current, {
      type: 'line',
      data: { datasets: [] },
      options: {
        maintainAspectRatio: false,
        interaction: { mode: 'nearest', intersect: false },
        plugins: {
          legend: { position: 'bottom', labels: { usePointStyle: true, boxWidth: 8, boxHeight: 8 } },
          tooltip: {
            usePointStyle: true,
            callbacks: {
              label: (context) => {
                const unit = context.dataset.unit ? ` ${context.dataset.unit}` : ''
                return `${context.dataset.label}: ${context.parsed.y}${unit}`
              },
            },
          },
        },
      },
    })
    chartRef.current = chart
    return () => chart.destroy()
  }, [])

  // Puts new data into the existing chart
  useEffect(() => {
    const chart = chartRef.current
    const { datasets, scales } = chartData(series, shapes, measurements, units)
    // Keep each series' dataset object, so its line is updated instead of redrawn
    const previous = new Map(chart.data.datasets.map((dataset) => [dataset.seriesId, dataset]))
    chart.data.datasets = datasets.map((dataset) => Object.assign(previous.get(dataset.seriesId) ?? {}, dataset))
    chart.options.scales = scales
    chart.update()
  }, [series, shapes, units, measurements])

  const seriesWithData = new Set(measurements.map((measurement) => measurement.seriesId))
  const empty = series.filter((item) => !seriesWithData.has(item.id))
  const description = `Line chart of ${series.map(seriesLabel).join(', ')} over time. The table below lists the same values.`
  return (
    <>
      <div className="chart-box">
        <canvas ref={canvasRef} role="img" aria-label={description} />
      </div>
      {empty.length > 0 && (
        <p className="small text-body-secondary mt-2 mb-0">
          No measurements in this time range: {empty.map(seriesLabel).join(', ')}.
        </p>
      )}
    </>
  )
}

export default MeasurementChart
