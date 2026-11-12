// Calls to the backend API.

const TOKEN_KEY = 'accessToken'

/** Error from the API, with a readable message and the field errors of an RFC 9457 response. */
export class ApiError extends Error {
  constructor(status, message, fieldErrors = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

/** Sends a request (with the admin token, if there is one) and returns the JSON body, or null for 204. */
export async function request(method, path, body) {
  const headers = { Accept: 'application/json' }
  const token = sessionStorage.getItem(TOKEN_KEY)
  if (token) {
    headers.Authorization = `Bearer ${token}`
  }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  let response
  try {
    response = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  } catch {
    throw new ApiError(0, 'Cannot connect to the server. Check your connection and try again.')
  }

  if (response.status === 204) {
    return null
  }
  const data = await response.json().catch(() => null)
  if (!response.ok) {
    throw new ApiError(response.status, errorMessage(response.status, data), fieldErrors(data))
  }
  return data
}

/** Builds a readable message from an RFC 9457 response, or from the status when there is none. */
function errorMessage(status, problem) {
  if (problem?.detail || problem?.title) {
    return problem.detail ?? problem.title
  }
  // A proxy answers 502 or 504 without a problem body when the backend is down
  return status >= 500 ? 'The server is not available right now. Try again later.' : `The request failed (status ${status}).`
}

/** Turns the errors list of an RFC 9457 response into {field: message}. */
function fieldErrors(problem) {
  return Object.fromEntries((problem?.errors ?? []).map((error) => [error.field, error.message]))
}

// The API returns at most this many measurements in one response
export const MEASUREMENT_LIMIT = 10000


export const getSeries = () => request('GET', '/api/series')

/** Gets the measurements of the given series in a time range (from and to are Date objects or null), newest first. */
export function getMeasurements(seriesIds, from, to) {
  const query = new URLSearchParams({ series: seriesIds.join(','), sort: '-timestamp', limit: MEASUREMENT_LIMIT })
  if (from) {
    query.set('from', from.toISOString())
  }
  if (to) {
    query.set('to', to.toISOString())
  }
  return request('GET', `/api/measurements?${query}`)
}
