import { useEffect, useState } from 'react'
import { getSeries } from './api.js'

function App() {
  const [series, setSeries] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  useEffect(() => {
    // Ignores the answer if the component is gone before it arrives
    let ignore = false
    getSeries()
      .then((data) => {
        if (!ignore) {
          setSeries(data)
        }
      })
      .catch((err) => {
        if (!ignore) {
          setError(err.message)
        }
      })
      .finally(() => {
        if (!ignore) {
          setLoading(false)
        }
      })
    return () => {
      ignore = true
    }
  }, [])

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
        <section className="card border-0 shadow-sm rounded-4">
          <div className="card-body p-4">
            <h2 className="h5 mb-3">Series</h2>

            {loading && <p role="status" className="text-body-secondary mb-0">Loading…</p>}
            {error && <div className="alert alert-danger mb-0" role="alert">{error}</div>}
            {!loading && !error && series.length === 0 && <p className="mb-0">No series yet.</p>}

            {series.length > 0 && (
              <ul className="list-group list-group-flush">
                {series.map((item) => (
                  <li key={item.id} className="list-group-item d-flex align-items-center gap-2 px-0">
                    <span className="series-swatch" style={{ backgroundColor: item.color }} aria-hidden="true" />
                    <span>{item.name}</span>
                    {item.unit && <span className="badge rounded-pill text-bg-light border ms-auto">{item.unit}</span>}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>
      </main>
    </>
  )
}

export default App
