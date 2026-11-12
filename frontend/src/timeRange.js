// Time ranges of the filters; "all" has no limit
export const PRESETS = [
  { id: '24h', label: '24 h', hours: 24 },
  { id: '7d', label: '7 days', hours: 24 * 7 },
  { id: '30d', label: '30 days', hours: 24 * 30 },
  { id: 'all', label: 'All', hours: null },
]

/** Turns the chosen range into the dates sent to the API (null = no limit), or an error message. */
export function rangeDates(range) {
  const preset = PRESETS.find((item) => item.id === range.preset)
  if (preset) {
    return { from: preset.hours ? new Date(Date.now() - preset.hours * 3600 * 1000) : null, to: null }
  }
  // Dates and times are local, e.g. "2026-11-10" and "14:30"; without a time the range covers whole days
  const from = range.fromDate ? new Date(`${range.fromDate}T${range.fromTime || '00:00'}`) : null
  const to = range.toDate ? new Date(`${range.toDate}T${range.toTime || '23:59'}:59.999`) : null
  if (from && to && from > to) {
    return { error: '"From" must be earlier than "To".' }
  }
  return { from, to }
}