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
  // datetime-local fields give the user's local time, e.g. "2026-11-10T14:30"
  const from = range.from ? new Date(range.from) : null
  const to = range.to ? new Date(range.to) : null
  if (from && to && from > to) {
    return { error: '"From" must be earlier than "To".' }
  }
  return { from, to }
}